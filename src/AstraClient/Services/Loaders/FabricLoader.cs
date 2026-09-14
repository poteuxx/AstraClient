using System.Text.Json.Serialization;
using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Loaders;

/// <summary>
/// Fabric loader: uses meta.fabricmc.net to list versions and build the profile JSON patch.
/// Supported for Minecraft 1.14+ (returns empty for older versions).
/// </summary>
public sealed class FabricLoader : ILoaderProvider
{
    private const string MetaBase = "https://meta.fabricmc.net/v2";
    private readonly HttpClientProvider _http;

    public FabricLoader(HttpClientProvider http) => _http = http;
    public string Name => "fabric";

    public async Task<List<LoaderVersion>> GetVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"{MetaBase}/versions/loader/{Uri.EscapeDataString(mcVersion)}";
            var entries = await _http.GetJsonAsync<List<FabricLoaderEntry>>(url, ct).ConfigureAwait(false);
            if (entries is null || entries.Count == 0) return [];

            return entries.Select((e, i) => new LoaderVersion(
                e.Loader?.Version ?? "",
                e.Loader?.Stable == true,
                i == 0)).ToList();
        }
        catch { return []; }
    }

    public async Task<VersionJson?> BuildPatchAsync(string mcVersion, string loaderVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"{MetaBase}/versions/loader/{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
            return await _http.GetJsonAsync<VersionJson>(url, ct).ConfigureAwait(false);
        }
        catch { return null; }
    }

    public string RecommendedJvmArgs(int memoryMb) =>
        $"-Xmx{memoryMb}M -Xms512M -XX:+UseG1GC -XX:+ParallelRefProcEnabled " +
        $"-XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC " +
        $"-XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M " +
        $"-XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:InitiatingHeapOccupancyPercent=15";

    public string RecommendedGameArgs() => "";

    private class FabricLoaderEntry
    {
        [JsonPropertyName("loader")]
        public FabricLoaderInfo? Loader { get; set; }
    }

    private class FabricLoaderInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }
}
