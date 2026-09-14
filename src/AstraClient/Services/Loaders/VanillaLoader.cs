using AstraClient.Models;

namespace AstraClient.Services.Loaders;

/// <summary>Vanilla passthrough — no patch, always available.</summary>
public sealed class VanillaLoader : ILoaderProvider
{
    public string Name => "vanilla";

    public Task<List<LoaderVersion>> GetVersionsAsync(string mcVersion, CancellationToken ct = default)
        => Task.FromResult(new List<LoaderVersion> { new("", true, true) });

    public Task<VersionJson?> BuildPatchAsync(string mcVersion, string loaderVersion, CancellationToken ct = default)
        => Task.FromResult<VersionJson?>(null);

    public string RecommendedJvmArgs(int memoryMb) =>
        $"-Xmx{memoryMb}M -Xms512M -XX:+UseG1GC -XX:+ParallelRefProcEnabled " +
        $"-XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC " +
        $"-XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M " +
        $"-XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 " +
        $"-XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 " +
        $"-XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem " +
        $"-XX:MaxTenuringThreshold=1 -Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true";

    public string RecommendedGameArgs() => "";
}
