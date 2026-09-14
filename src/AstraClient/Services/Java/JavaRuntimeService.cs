using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstraClient.Services.Http;
using AstraClient.Services.Launch;

namespace AstraClient.Services.Java;

/// <summary>
/// Detects installed JREs that match a required major version,
/// and can download Mojang-managed runtimes by component name.
/// </summary>
public sealed class JavaRuntimeService
{
    private readonly HttpClientProvider _http;
    private readonly string _runtimesDir;

    private const string MojangRuntimeManifest =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public JavaRuntimeService(HttpClientProvider http, string dataRoot)
    {
        _http = http;
        _runtimesDir = Path.Combine(dataRoot, "runtimes");
        Directory.CreateDirectory(_runtimesDir);
    }

    // ---- Detection -----------------------------------------------------------

    /// <summary>
    /// Finds the path to a java.exe matching the required major version.
    /// Returns null if none found (caller should then download Mojang runtime).
    /// </summary>
    public async Task<string?> DetectAsync(int requiredMajor, string? overridePath = null)
    {
        // 1. User-specified override.
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var ver = await GetVersionAsync(overridePath!).ConfigureAwait(false);
            if (ver == requiredMajor) return overridePath;
        }

        // 2. Check if a Mojang-managed runtime is already installed.
        var mojangPath = FindMojangRuntimeExe(requiredMajor);
        if (mojangPath is not null) return mojangPath;

        // 3. System PATH.
        foreach (var candidate in GetSystemJavaCandidates())
        {
            var ver = await GetVersionAsync(candidate).ConfigureAwait(false);
            if (ver == requiredMajor || (requiredMajor == 8 && ver == 8)) return candidate;
        }

        return null;
    }

    // ---- Download ------------------------------------------------------------

    /// <summary>
    /// Downloads the Mojang-managed JRE for the given component (e.g. "java-runtime-delta", "jre-legacy").
    /// Returns the path to java.exe on success.
    /// </summary>
    public async Task<string?> DownloadMojangRuntimeAsync(
        string component,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report((0, 0, $"Fetching Java runtime manifest ({component})…"));

            var allJson = await _http.GetJsonAsync<JsonElement>(MojangRuntimeManifest, ct)
                                     .ConfigureAwait(false);

            // Navigate: all["windows-x64"][component][0].manifest.url
            if (!allJson.TryGetProperty("windows-x64", out var winX64)) return null;
            if (!winX64.TryGetProperty(component, out var entries)) return null;
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0) return null;

            var manifestUrl = entries[0].GetProperty("manifest").GetProperty("url").GetString();
            if (string.IsNullOrEmpty(manifestUrl)) return null;

            var manifest = await _http.GetJsonAsync<RuntimeManifest>(manifestUrl, ct).ConfigureAwait(false);
            if (manifest?.Files is null) return null;

            var destDir = Path.Combine(_runtimesDir, component);
            Directory.CreateDirectory(destDir);

            int done = 0, total = manifest.Files.Count;
            var downloadables = manifest.Files
                .Where(kv => kv.Value.Type == "file" && kv.Value.Downloads?.Raw?.Url is not null)
                .ToList();

            total = downloadables.Count;

            foreach (var (relPath, file) in downloadables)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((++done, total, $"Downloading Java runtime {done}/{total}…"));

                var raw  = file.Downloads!.Raw!;
                var dest = Path.Combine(destDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                if (!File.Exists(dest) || (raw.Sha1 is not null && !LibraryResolver.VerifySha1(dest, raw.Sha1)))
                    await _http.DownloadFileAsync(raw.Url!, dest, ct: ct).ConfigureAwait(false);

                // Mark executables as executable on non-Windows.
                if (file.Executable == true && !OperatingSystem.IsWindows())
                {
                    try { Process.Start("chmod", $"+x \"{dest}\""); } catch { }
                }
            }

            return FindJavaExe(destDir);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[JavaRuntimeService] Download failed: {ex.Message}");
            return null;
        }
    }

    // ---- Helpers -------------------------------------------------------------

    private string? FindMojangRuntimeExe(int majorVersion)
    {
        if (!Directory.Exists(_runtimesDir)) return null;
        foreach (var dir in Directory.GetDirectories(_runtimesDir))
        {
            var exe = FindJavaExe(dir);
            if (exe is null) continue;
            var ver = GetVersionSync(exe);
            if (ver == majorVersion) return exe;
        }
        return null;
    }

    private static string? FindJavaExe(string runtimeDir)
    {
        // Standard layout: bin/java.exe (Windows) or bin/java (Linux/macOS).
        var exe = Path.Combine(runtimeDir, "bin", "java.exe");
        if (File.Exists(exe)) return exe;
        exe = Path.Combine(runtimeDir, "bin", "java");
        if (File.Exists(exe)) return exe;

        // Nested: sometimes there's a jdk-... subfolder.
        foreach (var sub in Directory.GetDirectories(runtimeDir))
        {
            var found = FindJavaExe(sub);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<string> GetSystemJavaCandidates()
    {
        // JAVA_HOME.
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            yield return Path.Combine(javaHome, "bin", "java.exe");

        // Well-known Windows install locations.
        var baseDirs = new[]
        {
            @"C:\Program Files\Java",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\BellSoft",
            @"C:\Program Files\Zulu",
        };
        foreach (var baseDir in baseDirs)
        {
            if (!Directory.Exists(baseDir)) continue;
            foreach (var jdk in Directory.GetDirectories(baseDir))
            {
                var exe = Path.Combine(jdk, "bin", "java.exe");
                if (File.Exists(exe)) yield return exe;
            }
        }

        // PATH.
        yield return "java";
        yield return "java.exe";
    }

    private static async Task<int> GetVersionAsync(string javaExe)
    {
        return await Task.Run(() => GetVersionSync(javaExe)).ConfigureAwait(false);
    }

    private static int GetVersionSync(string javaExe)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return 0;
            var output = proc.StandardError.ReadToEnd();
            proc.WaitForExit(3000);
            return ParseJavaMajorVersion(output);
        }
        catch { return 0; }
    }

    private static int ParseJavaMajorVersion(string output)
    {
        // openjdk version "21.0.3" 2024-04-16
        // java version "1.8.0_412"
        var match = System.Text.RegularExpressions.Regex.Match(output, @"""(\d+)[\.\-]");
        if (!match.Success) return 0;
        int major = int.Parse(match.Groups[1].Value);
        return major == 1 ? 8 : major; // "1.8" -> 8
    }

    // ---- Mojang runtime manifest models --------------------------------------

    private class RuntimeManifest
    {
        [JsonPropertyName("files")]
        public Dictionary<string, RuntimeFile>? Files { get; set; }
    }

    private class RuntimeFile
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "file"; // file | directory | link

        [JsonPropertyName("executable")]
        public bool? Executable { get; set; }

        [JsonPropertyName("downloads")]
        public RuntimeDownloads? Downloads { get; set; }
    }

    private class RuntimeDownloads
    {
        [JsonPropertyName("raw")]
        public RuntimeRaw? Raw { get; set; }
    }

    private class RuntimeRaw
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("sha1")]
        public string? Sha1 { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
