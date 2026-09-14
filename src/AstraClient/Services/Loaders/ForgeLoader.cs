using System.Text.Json.Serialization;
using System.Xml.Linq;
using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Loaders;

/// <summary>
/// Forge loader: uses files.minecraftforge.net promotions for version discovery.
/// Modern Forge install (processor pipeline) is best-effort; older versions download a -universal jar.
/// </summary>
public sealed class ForgeLoader : ILoaderProvider
{
    private const string PromotionsUrl =
        "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string MavenBase =
        "https://maven.minecraftforge.net/net/minecraftforge/forge";

    private readonly HttpClientProvider _http;
    private Dictionary<string, string>? _promotions; // "1.7.10-recommended" -> "10.13.4.1614"
    private readonly Dictionary<string, List<string>> _versionCache = new(StringComparer.OrdinalIgnoreCase);

    public ForgeLoader(HttpClientProvider http) => _http = http;
    public string Name => "forge";

    public async Task<List<LoaderVersion>> GetVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            _promotions ??= await FetchPromotionsAsync(ct).ConfigureAwait(false);
            var prefix = $"{mcVersion}-";
            var allVersions = await FetchMavenVersionsAsync(prefix, ct).ConfigureAwait(false);

            if (allVersions.Count == 0)
            {
                if (_promotions.TryGetValue($"{mcVersion}-recommended", out var rec))
                    allVersions.Add($"{prefix}{rec}");
                if (_promotions.TryGetValue($"{mcVersion}-latest", out var lat))
                    allVersions.Add($"{prefix}{lat}");
            }

            var recommended = _promotions.TryGetValue($"{mcVersion}-recommended", out var recommendedVersion)
                ? $"{prefix}{recommendedVersion}" : null;
            var latest = _promotions.TryGetValue($"{mcVersion}-latest", out var latestVersion)
                ? $"{prefix}{latestVersion}" : allVersions.FirstOrDefault();

            return allVersions.Select(version => new LoaderVersion(
                version,
                string.Equals(version, recommended, StringComparison.OrdinalIgnoreCase),
                string.Equals(version, latest, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
    }

    public async Task<VersionJson?> BuildPatchAsync(string mcVersion, string loaderVersion, CancellationToken ct = default)
    {
        try
        {
            // Attempt: download -universal.jar and unpack version.json from it (old-style).
            var jarUrl = $"{MavenBase}/{loaderVersion}/forge-{loaderVersion}-universal.jar";
            var jarPath = Path.GetTempFileName();
            try
            {
                await _http.DownloadFileAsync(jarUrl, jarPath, ct: ct).ConfigureAwait(false);
                var vj = ExtractFromJar(jarPath, "version.json");
                if (vj is not null) return vj;
            }
            catch { /* try installer */ }

            // Fallback: installer jar.
            var installerUrl = $"{MavenBase}/{loaderVersion}/forge-{loaderVersion}-installer.jar";
            await _http.DownloadFileAsync(installerUrl, jarPath, ct: ct).ConfigureAwait(false);
            return ExtractFromJar(jarPath, "version.json") ?? ExtractFromJar(jarPath, "install_profile.json");
        }
        catch { return null; }
    }

    public string RecommendedJvmArgs(int memoryMb) =>
        $"-Xmx{memoryMb}M -Xms512M -XX:+UseG1GC -XX:MaxGCPauseMillis=200 " +
        $"-XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=30";

    public string RecommendedGameArgs() => "";

    private async Task<Dictionary<string, string>> FetchPromotionsAsync(CancellationToken ct)
    {
        var result = await _http.GetJsonAsync<PromotionsResponse>(PromotionsUrl, ct).ConfigureAwait(false);
        return result?.Promos ?? [];
    }

    private async Task<List<string>> FetchMavenVersionsAsync(string prefix, CancellationToken ct)
    {
        if (_versionCache.TryGetValue(prefix, out var cached))
            return [.. cached];

        var xml = await _http.Client.GetStringAsync($"{MavenBase}/maven-metadata.xml", ct).ConfigureAwait(false);
        var versions = XDocument.Parse(xml)
            .Descendants("version")
            .Select(node => node.Value.Trim())
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(version => version, ForgeVersionComparer.Instance)
            .ToList();

        _versionCache[prefix] = versions;
        return [.. versions];
    }

    private sealed class ForgeVersionComparer : IComparer<string>
    {
        public static ForgeVersionComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var leftParts = left[(left.IndexOf('-') + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
            var rightParts = right[(right.IndexOf('-') + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
            {
                var leftPart = i < leftParts.Length ? leftParts[i] : "0";
                var rightPart = i < rightParts.Length ? rightParts[i] : "0";
                if (int.TryParse(leftPart, out var leftNumber) && int.TryParse(rightPart, out var rightNumber))
                {
                    var numeric = leftNumber.CompareTo(rightNumber);
                    if (numeric != 0) return numeric;
                }
                else
                {
                    var text = StringComparer.OrdinalIgnoreCase.Compare(leftPart, rightPart);
                    if (text != 0) return text;
                }
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }
    }

    private static VersionJson? ExtractFromJar(string jarPath, string entryName)
    {
        try
        {
            using var zip   = System.IO.Compression.ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry(entryName);
            if (entry is null) return null;
            using var stream = entry.Open();
            using var reader = new System.IO.StreamReader(stream);
            var json = reader.ReadToEnd();
            return System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json, Http.JsonHelper.Options);
        }
        catch { return null; }
    }

    private class PromotionsResponse
    {
        [JsonPropertyName("promos")]
        public Dictionary<string, string> Promos { get; set; } = [];
    }
}
