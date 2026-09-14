using AstraClient.Models;

namespace AstraClient.Services.Loaders;

public record LoaderVersion(string Version, bool IsRecommended, bool IsLatest)
{
    // Override so ComboBox SelectionBoxItem shows just the version string, not the full record dump.
    public override string ToString() => Version;
}

/// <summary>Implemented by each mod loader to provide version lists and version JSON patches.</summary>
public interface ILoaderProvider
{
    string Name { get; } // vanilla | fabric | quilt | neoforge | forge

    /// <summary>Returns available loader versions for the given MC version. Empty = not supported.</summary>
    Task<List<LoaderVersion>> GetVersionsAsync(string mcVersion, CancellationToken ct = default);

    /// <summary>Returns the version JSON patch (with inheritsFrom set) for a given loader+MC version combo.</summary>
    Task<VersionJson?> BuildPatchAsync(string mcVersion, string loaderVersion, CancellationToken ct = default);

    /// <summary>Recommended JVM arg preset for this loader.</summary>
    string RecommendedJvmArgs(int memoryMb);

    /// <summary>Recommended game arg preset (empty for most loaders).</summary>
    string RecommendedGameArgs();
}
