using System.Text.Json.Serialization;
using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Loaders;

/// <summary>
/// Quilt loader: uses meta.quiltmc.org — API mirrors Fabric's structure.
/// Supported for 1.14+ (same era as Fabric).
/// </summary>
public sealed class QuiltLoader : ILoaderProvider
{
    private const string MetaBase = "https://meta.quiltmc.org/v3";
    private readonly HttpClientProvider _http;

    public QuiltLoader(HttpClientProvider http) => _http = http;
    public string Name => "quilt";

    public async Task<List<LoaderVersion>> GetVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"{MetaBase}/versions/loader/{Uri.EscapeDataString(mcVersion)}";
            var entries = await _http.GetJsonAsync<List<QuiltLoaderEntry>>(url, ct).ConfigureAwait(false);
            if (entries is null || entries.Count == 0) return [];

            return entries.Select((e, i) => new LoaderVersion(
                e.Loader?.Version ?? "",
                true, // Quilt doesn't expose stable flag; treat all as recommended
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
        $"-Xmx{memoryMb}M -Xms512M -XX:+UseG1GC -XX:MaxGCPauseMillis=200 " +
        $"-XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=30";

    public string RecommendedGameArgs() => "";

    private class QuiltLoaderEntry
    {
        [JsonPropertyName("loader")]
        public QuiltLoaderInfo? Loader { get; set; }
    }

    private class QuiltLoaderInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }
}
