using AstraClient.Services.Http;

namespace AstraClient.Services.Loaders;

/// <summary>
/// Aggregates all loader providers and answers "which loaders are available for this MC version?"
/// Results are cached per MC version (network calls are expensive).
/// </summary>
public sealed class LoaderRegistry
{
    private readonly IReadOnlyList<ILoaderProvider> _providers;
    private readonly Dictionary<string, List<LoaderAvailability>> _cache = new();

    public LoaderRegistry(HttpClientProvider http)
    {
        _providers = [
            new VanillaLoader(),
            new FabricLoader(http),
            new QuiltLoader(http),
            new NeoForgeLoader(http),
            new ForgeLoader(http),
        ];
    }

    public IReadOnlyList<ILoaderProvider> All => _providers;

    public ILoaderProvider? Get(string name)
        => _providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns availability info for all loaders for the given MC version.
    /// Fires parallel requests; first call per version may take a few seconds.
    /// </summary>
    public async Task<List<LoaderAvailability>> GetAvailabilityAsync(
        string mcVersion, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(mcVersion, out var cached)) return cached;

        var tasks = _providers.Select(async p =>
        {
            try
            {
                var versions = await p.GetVersionsAsync(mcVersion, ct).ConfigureAwait(false);
                return new LoaderAvailability
                {
                    LoaderName     = p.Name,
                    Provider       = p,
                    IsSupported    = versions.Count > 0,
                    Recommended    = versions.FirstOrDefault(v => v.IsRecommended),
                    Latest         = versions.FirstOrDefault(v => v.IsLatest),
                    AllVersions    = versions
                };
            }
            catch
            {
                return new LoaderAvailability { LoaderName = p.Name, Provider = p };
            }
        });

        var result = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
        _cache[mcVersion] = result;
        return result;
    }
}

public class LoaderAvailability
{
    public string LoaderName { get; set; } = "";
    public ILoaderProvider Provider { get; set; } = null!;
    public bool IsSupported { get; set; }
    public LoaderVersion? Recommended { get; set; }
    public LoaderVersion? Latest { get; set; }
    public List<LoaderVersion> AllVersions { get; set; } = [];

    public string BadgeText => IsSupported
        ? (Recommended is not null ? "Recommended" : "Available")
        : "Not Supported";
}
