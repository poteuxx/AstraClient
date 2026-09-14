using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using AstraClient.Models;
using AstraClient.Services.Data;
using AstraClient.Services.Http;
using AstraClient.Services.Java;
using AstraClient.Services.Loaders;
using AstraClient.Services.Mojang;

namespace AstraClient.Services.Launch;

/// <summary>
/// Orchestrates the full Minecraft launch pipeline:
/// resolve → download client/libs/assets/natives → Java → classpath/args → spawn.
/// </summary>
public sealed class LaunchService
{
    private readonly HttpClientProvider _http;
    private readonly MojangService      _mojang;
    private readonly JavaRuntimeService _java;
    private readonly LibraryService     _libraryService;
    private readonly InstanceService    _instances;
    private readonly LoaderRegistry     _loaderRegistry;
    private readonly string             _dataRoot;
    private readonly object _processLock = new();
    private Process? _gameProcess;
    private string? _gameInstanceId;

    private string VersionsDir  => Path.Combine(_dataRoot, "versions");
    private string LibrariesDir => Path.Combine(_dataRoot, "libraries");
    private string AssetsDir    => Path.Combine(_dataRoot, "assets");

    public LaunchService(
        HttpClientProvider http,
        MojangService mojang,
        JavaRuntimeService java,
        LibraryService library,
        InstanceService instances,
        LoaderRegistry loaderRegistry,
        string dataRoot)
    {
        _http           = http;
        _mojang         = mojang;
        _java           = java;
        _libraryService = library;
        _instances      = instances;
        _loaderRegistry = loaderRegistry;
        _dataRoot       = dataRoot;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Full launch pipeline. Reports granular progress. Throws on unrecoverable errors.
    /// </summary>

    public event Action<string, string>? LogReceived;
    public event Action<string, bool>? ProcessStateChanged;

    public bool IsRunning(string instanceId)
    {
        lock (_processLock)
            return _gameInstanceId == instanceId && _gameProcess is { HasExited: false };
    }

    public bool Stop(string instanceId)
    {
        Process? process;
        lock (_processLock)
        {
            if (_gameInstanceId != instanceId || _gameProcess is not { HasExited: false })
                return false;
            process = _gameProcess;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
    public async Task LaunchAsync(
        Instance instance,
        UserProfile profile,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        void Report(LaunchStage stage, string msg, int cur = 0, int total = 0, double pct = 0)
            => progress?.Report(OperationProgress.Of(stage, msg, cur, total, pct));

        // ── 1. Resolve version JSON ─────────────────────────────────────────
        Report(LaunchStage.ResolvingVersion, $"Preparing {instance.GameVersion} ({instance.Loader})…");
        var manifest = await _mojang.GetManifestAsync(false, ct).ConfigureAwait(false);
        var entry    = manifest.Versions.FirstOrDefault(v => v.Id == instance.GameVersion)
                       ?? throw new InvalidOperationException($"Version {instance.GameVersion} not found in manifest.");

        var baseJson = await _mojang.GetVersionJsonAsync(entry.Url, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException($"Failed to fetch version JSON for {instance.GameVersion}.");

        // Cache base version JSON locally
        var verDir      = Path.Combine(VersionsDir, instance.GameVersion);
        Directory.CreateDirectory(verDir);
        var verJsonPath = Path.Combine(verDir, $"{instance.GameVersion}.json");
        if (!File.Exists(verJsonPath))
        {
            var rawJson = await _http.Client.GetStringAsync(entry.Url, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(verJsonPath, rawJson, ct).ConfigureAwait(false);
        }

        // Apply loader patch if not vanilla
        VersionJson resolvedJson = baseJson;
        if (!string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
                throw new InvalidOperationException(
                    $"Instance '{instance.Name}' has loader {instance.Loader} but no loader version.");

            var loaderProvider = _loaderRegistry.Get(instance.Loader);
            if (loaderProvider != null)
            {
                var patch = string.Equals(instance.Loader, "neoforge", StringComparison.OrdinalIgnoreCase)
                    ? await InstallNeoForgeClientAsync(instance, baseJson, Report, ct).ConfigureAwait(false)
                    : await loaderProvider.BuildPatchAsync(
                        instance.GameVersion, instance.LoaderVersion, ct).ConfigureAwait(false);
                if (patch is null)
                    throw new InvalidOperationException(
                        $"Could not resolve the {instance.Loader} {instance.LoaderVersion} profile for Minecraft {instance.GameVersion}.");

                resolvedJson = await _mojang.MergeInheritsFromAsync(patch, VersionsDir, ct).ConfigureAwait(false)
                               ?? throw new InvalidOperationException(
                                   $"Could not resolve the Minecraft parent profile for {instance.Loader} {instance.LoaderVersion}.");
            }
            else
            {
                throw new InvalidOperationException($"Unsupported loader: {instance.Loader}.");
            }
        }

        // ── 2. Download client JAR ──────────────────────────────────────────
        Report(LaunchStage.DownloadingClient, "Downloading client jar…", 0, 1, 5);
        var clientJarPath = Path.Combine(verDir, $"{instance.GameVersion}.jar");
        if (resolvedJson.Downloads?.Client is not { } clientDl || string.IsNullOrWhiteSpace(clientDl.Url))
            throw new InvalidOperationException($"Minecraft {instance.GameVersion} does not provide a client download.");

        if (!File.Exists(clientJarPath)
            || (clientDl.Sha1 is not null && !LibraryResolver.VerifySha1(clientJarPath, clientDl.Sha1)))
        {
            await _http.DownloadFileAsync(clientDl.Url, clientJarPath, ct: ct).ConfigureAwait(false);
        }

        // ── 3. Download libraries ───────────────────────────────────────────
        var libResolver = new LibraryResolver(_http, _dataRoot);
        var libraries   = resolvedJson.Libraries
            .Where(lib => RulesEvaluator.Evaluate(lib.Rules))
            .ToList();

        Report(LaunchStage.DownloadingLibraries, $"Fetching libraries 0/{libraries.Count}…", 0, libraries.Count, 10);
        var classPaths = new List<string>();
        var nativeJars = new List<string>();
        int libDone    = 0;

        foreach (var lib in libraries)
        {
            ct.ThrowIfCancellationRequested();

            var path = await libResolver.ResolveAsync(lib, nativesOnly: false, ct: ct).ConfigureAwait(false);
            if (path != null) classPaths.Add(path);

            var nativePath = await libResolver.ResolveNativeAsync(lib, ct).ConfigureAwait(false);
            if (nativePath != null) nativeJars.Add(nativePath);

            libDone++;
            Report(LaunchStage.DownloadingLibraries,
                $"Fetching libraries {libDone}/{libraries.Count}…",
                libDone, libraries.Count, 10 + libDone * 20.0 / Math.Max(libraries.Count, 1));
        }

        // ── 4. Download asset index + objects ────────────────────────────────
        Report(LaunchStage.DownloadingAssets, "Downloading asset index…", 0, 0, 30);
        string assetIndexId = resolvedJson.AssetIndex?.Id ?? instance.GameVersion;

        if (resolvedJson.AssetIndex is { } assetIdx)
        {
            var indexDir  = Path.Combine(AssetsDir, "indexes");
            var indexPath = Path.Combine(indexDir, $"{assetIdx.Id}.json");
            Directory.CreateDirectory(indexDir);

            if (!File.Exists(indexPath)
                || (assetIdx.Sha1 is not null && !LibraryResolver.VerifySha1(indexPath, assetIdx.Sha1)))
                await _http.DownloadFileAsync(assetIdx.Url, indexPath, ct: ct).ConfigureAwait(false);

            var assetObjs = await _mojang.GetAssetIndexObjectsAsync(assetIdx.Url, ct).ConfigureAwait(false);

            if (assetObjs?.Objects is { Count: > 0 } objects)
            {
                var objectsDir = Path.Combine(AssetsDir, "objects");
                Directory.CreateDirectory(objectsDir);

                int assetDone  = 0;
                int assetTotal = objects.Count;
                Report(LaunchStage.DownloadingAssets, $"Downloading assets 0/{assetTotal}…", 0, assetTotal, 32);

                var semaphore = new SemaphoreSlim(8);
                var tasks     = objects.Values.Select(async obj =>
                {
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var prefix  = obj.Hash.Substring(0, 2);
                        var objDir  = Path.Combine(objectsDir, prefix);
                        var objPath = Path.Combine(objDir, obj.Hash);
                        Directory.CreateDirectory(objDir);

                        if (!File.Exists(objPath) || !LibraryResolver.VerifySha1(objPath, obj.Hash))
                        {
                            var url = $"https://resources.download.minecraft.net/{prefix}/{obj.Hash}";
                            await _http.DownloadFileAsync(url, objPath, ct: ct).ConfigureAwait(false);
                        }

                        int done = System.Threading.Interlocked.Increment(ref assetDone);
                        if (done % 100 == 0 || done == assetTotal)
                            Report(LaunchStage.DownloadingAssets,
                                $"Downloading assets {done}/{assetTotal}…",
                                done, assetTotal, 32 + done * 28.0 / assetTotal);
                    }
                    finally { semaphore.Release(); }
                });
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        // ── 5. Extract natives ───────────────────────────────────────────────
        Report(LaunchStage.ExtractingNatives, "Extracting natives…");
        var nativesDir = Path.Combine(verDir, "natives");
        Directory.CreateDirectory(nativesDir);
        ExtractNatives(nativeJars, nativesDir, resolvedJson.Libraries);

        // ── 6. Resolve Java ──────────────────────────────────────────────────
        int    requiredMajor  = resolvedJson.JavaVersion?.MajorVersion ?? 8;
        string javaComponent  = resolvedJson.JavaVersion?.Component ?? "jre-legacy";
        Report(LaunchStage.ResolvingJava, $"Resolving Java {requiredMajor}…");

        string? javaExe = string.IsNullOrEmpty(instance.JavaOverride)
            ? await _java.DetectAsync(requiredMajor).ConfigureAwait(false)
            : instance.JavaOverride;

        if (string.IsNullOrEmpty(javaExe))
        {
            Report(LaunchStage.ResolvingJava, $"Downloading Mojang JRE ({javaComponent})…");
            javaExe = await _java.DownloadMojangRuntimeAsync(javaComponent,
                new Progress<(int cur, int total, string msg)>(p =>
                    Report(LaunchStage.ResolvingJava, p.msg, p.cur, p.total)),
                ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(javaExe))
            throw new InvalidOperationException(
                $"Could not find or download Java {requiredMajor}. Please install it or set a path in Settings.");

        // ── 7. Build classpath ───────────────────────────────────────────────
        Report(LaunchStage.BuildingArgs, "Building classpath & arguments…");
        classPaths.Add(clientJarPath);
        var classpath = string.Join(Path.PathSeparator.ToString(),
            classPaths
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        // ── 8. Build args ────────────────────────────────────────────────────
        var gameDir = instance.GameDir;
        Directory.CreateDirectory(gameDir);

        var jvmArgs  = ArgumentBuilder.BuildJvmArgs(
            resolvedJson, nativesDir, classpath, gameDir,
            AssetsDir, LibrariesDir, assetIndexId, instance.MemoryMb, instance.JvmArgs,
            instance.GameVersion);

        var gameArgs = ArgumentBuilder.BuildGameArgs(
            resolvedJson,
            profile.Username, profile.Uuid, profile.AccessToken,
            gameDir, AssetsDir, assetIndexId, resolvedJson.Type,
            instance.GameArgs, instance.GameVersion);

        var allArgs = new List<string>(jvmArgs) { resolvedJson.MainClass };
        allArgs.AddRange(gameArgs);

        var unresolved = allArgs.FirstOrDefault(argument => argument.Contains("${", StringComparison.Ordinal));
        if (unresolved is not null)
            throw new InvalidOperationException(
                $"The {instance.Loader} launch profile contains an unresolved token: {unresolved}");

        Console.WriteLine($"[LaunchService] java=\"{javaExe}\" mainClass={resolvedJson.MainClass}");

        // ── 9. Spawn process ─────────────────────────────────────────────────
        Report(LaunchStage.Launching, "Launching Minecraft…");
        var psi = new ProcessStartInfo
        {
            FileName               = javaExe,
            WorkingDirectory       = gameDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        foreach (var arg in allArgs) psi.ArgumentList.Add(arg);

        var proc = Process.Start(psi)
                   ?? throw new InvalidOperationException("Failed to start Minecraft process.");

        lock (_processLock)
        {
            _gameProcess = proc;
            _gameInstanceId = instance.Id;
        }
        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) =>
        {
            lock (_processLock)
            {
                if (ReferenceEquals(_gameProcess, proc))
                {
                    _gameProcess = null;
                    _gameInstanceId = null;
                }
            }
            ProcessStateChanged?.Invoke(instance.Id, false);
        };
        ProcessStateChanged?.Invoke(instance.Id, true);

        var launchOutput = new StringBuilder();
        var launchLogPath = Path.Combine(gameDir, "logs", "astra-launch.log");
        var logLock = new object();
        Directory.CreateDirectory(Path.GetDirectoryName(launchLogPath)!);
        await File.AppendAllTextAsync(launchLogPath,
            $"[{DateTime.UtcNow:O}] Starting {instance.Loader} {instance.LoaderVersion} for Minecraft {instance.GameVersion}{Environment.NewLine}", ct)
            .ConfigureAwait(false);

        void PublishLog(string line, bool isError)
        {
            var formatted = $"[{DateTime.Now:HH:mm:ss}] {(isError ? "ERR" : "OUT")} {line}";
            lock (logLock)
            {
                launchOutput.AppendLine(formatted);
                File.AppendAllText(launchLogPath, formatted + Environment.NewLine);
            }
            LogReceived?.Invoke(instance.Id, formatted);
        }

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                PublishLog(e.Data, isError: false);
                Console.WriteLine($"[MC] {e.Data}");
                progress?.Report(OperationProgress.Of(LaunchStage.Running, e.Data));
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                PublishLog(e.Data, isError: true);
                Console.Error.WriteLine($"[MC ERR] {e.Data}");
            }
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        if (proc.HasExited)
        {
            proc.WaitForExit();
            var output = launchOutput.ToString().Trim();
            await File.AppendAllTextAsync(launchLogPath,
                $"[{DateTime.UtcNow:O}] Process exited with code {proc.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}",
                CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(output)
                    ? $"Minecraft exited during startup with code {proc.ExitCode}. See {launchLogPath}."
                    : $"Minecraft exited during startup: {output.Split(Environment.NewLine).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))}");
        }

        Report(LaunchStage.Running, "Minecraft is running…", 0, 0, 100);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<VersionJson?> InstallNeoForgeClientAsync(
        Instance instance, VersionJson baseJson,
        Action<LaunchStage, string, int, int, double> report,
        CancellationToken ct)
    {
        var profileId = $"neoforge-{instance.LoaderVersion}";
        var profilePath = Path.Combine(VersionsDir, profileId, $"{profileId}.json");
        if (File.Exists(profilePath))
        {
            var cached = await File.ReadAllTextAsync(profilePath, ct).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<VersionJson>(cached, Http.JsonHelper.Options);
        }

        var requiredJava = baseJson.JavaVersion?.MajorVersion ?? 21;
        var java = await _java.DetectAsync(requiredJava).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(java))
            java = await _java.DownloadMojangRuntimeAsync(
                baseJson.JavaVersion?.Component ?? "java-runtime-delta", ct: ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(java))
            throw new InvalidOperationException($"Java {requiredJava} is required to install NeoForge.");

        var installer = Path.Combine(Path.GetTempPath(), $"astra-neoforge-{Guid.NewGuid():N}.jar");
        try
        {
            var launcherProfilesPath = Path.Combine(_dataRoot, "launcher_profiles.json");
            if (!File.Exists(launcherProfilesPath))
            {
                await File.WriteAllTextAsync(
                    launcherProfilesPath,
                    "{\"profiles\":{},\"selectedProfile\":\"\"}",
                    ct).ConfigureAwait(false);
            }

            var installerUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/" +
                               $"{instance.LoaderVersion}/neoforge-{instance.LoaderVersion}-installer.jar";
            await _http.DownloadFileAsync(installerUrl, installer, ct: ct).ConfigureAwait(false);

            var installerStartInfo = new ProcessStartInfo(java)
            {
                WorkingDirectory = _dataRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            installerStartInfo.ArgumentList.Add("-jar");
            installerStartInfo.ArgumentList.Add(installer);
            installerStartInfo.ArgumentList.Add("--install-client");
            installerStartInfo.ArgumentList.Add(_dataRoot);
            using var process = Process.Start(installerStartInfo)
                ?? throw new InvalidOperationException("Could not start the NeoForge installer.");

            report(LaunchStage.ResolvingVersion,
                $"Installing NeoForge {instance.LoaderVersion} client profile…", 0, 1, 2);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"NeoForge client installation failed: {stderr.Trim()}" +
                    (string.IsNullOrWhiteSpace(stdout) ? "" : $" {stdout.Trim()}"));
            }

            report(LaunchStage.ResolvingVersion,
                $"NeoForge {instance.LoaderVersion} client profile ready.", 1, 1, 4);

            if (!File.Exists(profilePath))
                throw new FileNotFoundException("NeoForge installer did not create the client profile.", profilePath);

            var json = await File.ReadAllTextAsync(profilePath, ct).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json, Http.JsonHelper.Options);
        }
        finally
        {
            try { if (File.Exists(installer)) File.Delete(installer); } catch { }
        }
    }

    private static void ExtractNatives(List<string> nativeJars, string nativesDir, List<Library> libraries)
    {
        foreach (var jar in nativeJars)
        {
            if (!File.Exists(jar)) continue;
            try
            {
                using var archive = ZipFile.OpenRead(jar);
                var artName  = Path.GetFileNameWithoutExtension(jar);
                var lib      = libraries.FirstOrDefault(l =>
                    artName.Contains(l.Name.Split(':').ElementAtOrDefault(1) ?? "XXX"));
                var excludes = lib?.Extract?.Exclude ?? new List<string> { "META-INF/" };

                foreach (var zipEntry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(zipEntry.Name)) continue;
                    if (excludes.Any(ex =>
                        zipEntry.FullName.StartsWith(ex, StringComparison.OrdinalIgnoreCase))) continue;
                    var dest = Path.Combine(nativesDir, zipEntry.Name);
                    zipEntry.ExtractToFile(dest, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LaunchService] Natives extract failed: {jar}: {ex.Message}");
            }
        }
    }
}
