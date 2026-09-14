using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AstraClient.Models;
using AstraClient.Services.Http;
using AstraClient.Services.Java;
using AstraClient.Services.Mojang;

namespace AstraClient.Services.Data;

/// <summary>
/// Owns the full lifecycle of locally hosted servers. Each server is isolated in
/// its own directory under Astra's data root; no game instance files are shared.
/// </summary>
public sealed class LocalServerService
{
    private readonly string _root;
    private readonly string _storePath;
    private readonly HttpClientProvider _http;
    private readonly MojangService _mojang;
    private readonly JavaRuntimeService _java;
    private readonly Dictionary<string, Process> _running = new();

    public event EventHandler<(string ServerId, string Line)>? OutputReceived;
    public event EventHandler<string>? ServerStopped;

    public LocalServerService(string dataRoot, HttpClientProvider http, MojangService mojang, JavaRuntimeService java)
    {
        _root = Path.Combine(dataRoot, "servers");
        _storePath = Path.Combine(dataRoot, "servers.json");
        _http = http;
        _mojang = mojang;
        _java = java;
        Directory.CreateDirectory(_root);
    }

    public async Task<List<LocalServer>> LoadAsync()
    {
        var list = await JsonStore.LoadAsync<List<LocalServer>>(_storePath).ConfigureAwait(false);
        return list ?? [];
    }
    public Task SaveAsync(IEnumerable<LocalServer> servers) => JsonStore.SaveAsync(_storePath, servers.ToList());
    public string GetDirectory(LocalServer server) => Path.Combine(_root, server.Id);
    public bool IsRunning(LocalServer server) => _running.TryGetValue(server.Id, out var p) && !p.HasExited;

    public async Task ProvisionAsync(LocalServer server, CancellationToken ct = default)
    {
        var directory = GetDirectory(server);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "mods"));
        Directory.CreateDirectory(Path.Combine(directory, "plugins"));
        Directory.CreateDirectory(Path.Combine(directory, "backups"));
        WriteProperties(server);

        var serverJar = Path.Combine(directory, "server.jar");
        switch (server.Type)
        {
            case LocalServerType.Vanilla:
                await DownloadVanillaAsync(server.GameVersion, serverJar, ct).ConfigureAwait(false);
                break;
            case LocalServerType.Fabric:
                await _http.DownloadFileAsync($"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(server.GameVersion)}/latest/latest/server/jar", serverJar, ct: ct).ConfigureAwait(false);
                break;
            case LocalServerType.Paper:
                await DownloadPaperAsync(server.GameVersion, serverJar, ct).ConfigureAwait(false);
                break;
            case LocalServerType.Forge:
            case LocalServerType.NeoForge:
                await InstallModLoaderAsync(server, directory, ct).ConfigureAwait(false);
                break;
        }
    }

    public async Task StartAsync(LocalServer server, string? javaOverride = null, CancellationToken ct = default)
    {
        if (IsRunning(server)) return;
        if (!server.EulaAccepted) throw new InvalidOperationException("Accept the Minecraft EULA before starting this server.");

        var directory = GetDirectory(server);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Provision this server before starting it.");
        var java = await _java.DetectAsync(GetRequiredJava(server.GameVersion), javaOverride).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A compatible Java runtime was not found. Set one in Settings.");

        ProcessStartInfo startInfo;
        var runBat = Path.Combine(directory, "run.bat");
        if ((server.Type is LocalServerType.Forge or LocalServerType.NeoForge) && File.Exists(runBat))
        {
            startInfo = new ProcessStartInfo("cmd.exe", "/c run.bat nogui") { WorkingDirectory = directory };
        }
        else
        {
            var jar = Path.Combine(directory, "server.jar");
            if (!File.Exists(jar)) throw new FileNotFoundException("Server JAR not found. Provision the server first.", jar);
            startInfo = new ProcessStartInfo(java, $"-Xms{server.MemoryMb}M -Xmx{server.MemoryMb}M -jar server.jar nogui") { WorkingDirectory = directory };
        }

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the server process.");
        _running[server.Id] = process;
        server.LastStarted = DateTime.UtcNow;
        process.OutputDataReceived += (_, e) => Publish(server.Id, e.Data);
        process.ErrorDataReceived += (_, e) => Publish(server.Id, e.Data);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => { _running.Remove(server.Id); ServerStopped?.Invoke(this, server.Id); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public void SendCommand(LocalServer server, string command)
    {
        if (IsRunning(server) && !string.IsNullOrWhiteSpace(command))
            _running[server.Id].StandardInput.WriteLine(command.Trim());
    }

    public void Stop(LocalServer server) => SendCommand(server, "stop");

    public async Task RestartAsync(LocalServer server, CancellationToken ct = default)
    {
        if (IsRunning(server))
        {
            Stop(server);
            for (var attempt = 0; attempt < 30 && IsRunning(server); attempt++)
                await Task.Delay(250, ct).ConfigureAwait(false);
        }

        await StartAsync(server, ct: ct).ConfigureAwait(false);
    }

    public async Task<string> BackupAsync(LocalServer server, CancellationToken ct = default)
    {
        var directory = GetDirectory(server);
        var world = Path.Combine(directory, "world");
        if (!Directory.Exists(world)) throw new DirectoryNotFoundException("Start the server once to create its world before backing it up.");
        SendCommand(server, "save-all");
        var target = Path.Combine(directory, "backups", $"world-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
        await Task.Run(() => ZipFile.CreateFromDirectory(world, target), ct).ConfigureAwait(false);
        return target;
    }

    public void WriteProperties(LocalServer server)
    {
        var values = new Dictionary<string, string>
        {
            ["server-port"] = server.Port.ToString(), ["max-players"] = server.MaxPlayers.ToString(),
            ["motd"] = server.Motd, ["online-mode"] = server.OnlineMode.ToString().ToLowerInvariant(),
            ["pvp"] = server.Pvp.ToString().ToLowerInvariant(), ["difficulty"] = server.Difficulty,
            ["gamemode"] = server.GameMode, ["enable-command-block"] = "false", ["white-list"] = "false"
        };
        var content = string.Join(Environment.NewLine, values.Select(pair => $"{pair.Key}={pair.Value}"));
        File.WriteAllText(Path.Combine(GetDirectory(server), "server.properties"), content + Environment.NewLine, Encoding.UTF8);
        File.WriteAllText(Path.Combine(GetDirectory(server), "eula.txt"), $"eula={server.EulaAccepted.ToString().ToLowerInvariant()}" + Environment.NewLine, Encoding.UTF8);
    }

    private async Task DownloadVanillaAsync(string version, string destination, CancellationToken ct)
    {
        var manifest = await _mojang.GetManifestAsync(ct: ct).ConfigureAwait(false);
        var entry = manifest.Versions.FirstOrDefault(item => item.Id == version)
            ?? throw new InvalidOperationException($"Minecraft {version} is not available from Mojang.");
        var metadata = await _mojang.GetVersionJsonAsync(entry.Url, ct).ConfigureAwait(false);
        var download = metadata?.Downloads?.Server ?? throw new InvalidOperationException($"Mojang does not publish a dedicated server for {version}.");
        await _http.DownloadFileAsync(download.Url, destination, ct: ct).ConfigureAwait(false);
    }

    private async Task DownloadPaperAsync(string version, string destination, CancellationToken ct)
    {
        using var response = await _http.Client.GetAsync($"https://fill.papermc.io/v3/projects/paper/versions/{Uri.EscapeDataString(version)}/builds", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        var stable = json.RootElement.EnumerateArray().FirstOrDefault(build =>
            build.TryGetProperty("channel", out var channel) && channel.GetString() == "STABLE");
        if (stable.ValueKind == JsonValueKind.Undefined || !stable.TryGetProperty("downloads", out var downloads) ||
            !downloads.TryGetProperty("server:default", out var server) || !server.TryGetProperty("url", out var url))
            throw new InvalidOperationException($"Paper has no stable build for Minecraft {version}.");
        await _http.DownloadFileAsync(url.GetString()!, destination, ct: ct).ConfigureAwait(false);
    }

    private async Task InstallModLoaderAsync(LocalServer server, string directory, CancellationToken ct)
    {
        if (!Uri.TryCreate(server.InstallerUrl, UriKind.Absolute, out var installerUri) || installerUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Forge and NeoForge require the official HTTPS installer URL for the selected build.");
        var installer = Path.Combine(directory, "installer.jar");
        await _http.DownloadFileAsync(installerUri.AbsoluteUri, installer, ct: ct).ConfigureAwait(false);
        var java = await _java.DetectAsync(GetRequiredJava(server.GameVersion)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A compatible Java runtime is required to install this server.");
        using var process = Process.Start(new ProcessStartInfo(java, "-jar installer.jar --installServer")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start the mod-loader installer.");
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException("The mod-loader installer failed. See its files in the server folder.");
    }

    private void Publish(string serverId, string? line) { if (!string.IsNullOrWhiteSpace(line)) OutputReceived?.Invoke(this, (serverId, line)); }
    private static int GetRequiredJava(string version) => Version.TryParse(version, out var parsed) && (parsed.Major > 1 || parsed.Minor >= 20 && parsed.Build >= 5) ? 21 : Version.TryParse(version, out parsed) && parsed.Minor >= 17 ? 17 : 8;
}
