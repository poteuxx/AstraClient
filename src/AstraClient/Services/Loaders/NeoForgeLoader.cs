using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Loaders;

/// <summary>
/// NeoForge loader: supports MC 1.20.2+ (version prefix 20.x).
/// Uses maven.neoforged.net metadata.
/// </summary>
public sealed class NeoForgeLoader : ILoaderProvider
{
    private const string MavenBase = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private readonly HttpClientProvider _http;

    public NeoForgeLoader(HttpClientProvider http) => _http = http;
    public string Name => "neoforge";

    public async Task<List<LoaderVersion>> GetVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            // NeoForge version format: {mcMajor.mcMinor}.{nfPatch}  e.g. 20.4.xyz for MC 1.20.4
            var parts = mcVersion.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int major) ||
                !int.TryParse(parts[1], out int minor) || major < 1 || (major == 1 && minor < 20))
                return [];

            // Build prefix like "20.4." from "1.20.4"
            var mcPrefix = minor >= 1 ? $"{minor}.{(parts.Length > 2 ? parts[2] : "0")}." : $"{minor}.";
            var metaUrl  = $"{MavenBase}/maven-metadata.xml";
            var xml = await _http.Client.GetStringAsync(metaUrl, ct).ConfigureAwait(false);

            // Parse <version> tags filtering for our MC prefix.
            var versions = new List<string>();
            int pos = 0;
            while (true)
            {
                int start = xml.IndexOf("<version>", pos, StringComparison.Ordinal);
                if (start < 0) break;
                start += "<version>".Length;
                int end = xml.IndexOf("</version>", start, StringComparison.Ordinal);
                if (end < 0) break;
                var v = xml.Substring(start, end - start).Trim();
                if (v.StartsWith(mcPrefix, StringComparison.Ordinal)) versions.Add(v);
                pos = end;
            }

            versions.Sort(StringComparer.OrdinalIgnoreCase);
            versions.Reverse();
            return versions.Select((v, i) => new LoaderVersion(v, i == 0, i == 0)).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
    }

    public async Task<VersionJson?> BuildPatchAsync(string mcVersion, string loaderVersion, CancellationToken ct = default)
    {
        try
        {
            // NeoForge publishes a version.json inside the installer jar.
            // We fetch the installer and extract version.json from the ZIP.
            var jarUrl  = $"{MavenBase}/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";
            var jarPath = Path.GetTempFileName();
            try
            {
                await _http.DownloadFileAsync(jarUrl, jarPath, ct: ct).ConfigureAwait(false);
                return ExtractVersionJsonFromJar(jarPath);
            }
            finally
            {
                try { File.Delete(jarPath); } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static VersionJson? ExtractVersionJsonFromJar(string jarPath)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry("version.json") ?? zip.GetEntry("install_profile.json");
            if (entry is null) return null;
            using var stream = entry.Open();
            using var reader = new System.IO.StreamReader(stream);
            var json = reader.ReadToEnd();
            return System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json, Http.JsonHelper.Options);
        }
        catch { return null; }
    }

    public string RecommendedJvmArgs(int memoryMb) =>
        $"-Xmx{memoryMb}M -Xms512M -XX:+UseG1GC -XX:MaxGCPauseMillis=200 " +
        $"-XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=30 " +
        $"-XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M";

    public string RecommendedGameArgs() => "";
}
