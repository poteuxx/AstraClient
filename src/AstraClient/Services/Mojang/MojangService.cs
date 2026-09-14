using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Mojang;

public sealed class MojangService
{
    private const string ManifestUrl =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly HttpClientProvider _http;
    private VersionManifest? _cachedManifest;

    public MojangService(HttpClientProvider http) => _http = http;

    /// <summary>Returns the full version manifest (cached in memory after first fetch).</summary>
    public async Task<VersionManifest> GetManifestAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cachedManifest is not null)
            return _cachedManifest;

        try
        {
            var manifest = await _http.GetJsonAsync<VersionManifest>(ManifestUrl, ct).ConfigureAwait(false);
            if (manifest?.Versions is { Count: > 0 })
            {
                _cachedManifest = manifest;
                return manifest;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* fall through to offline seed */ }

        return OfflineSeed();
    }

    /// <summary>Downloads and parses a version JSON by its URL.</summary>
    public async Task<VersionJson?> GetVersionJsonAsync(string url, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetJsonAsync<VersionJson>(url, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>Downloads the asset index objects JSON.</summary>
    public async Task<AssetIndexObjects?> GetAssetIndexObjectsAsync(string url, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetJsonAsync<AssetIndexObjects>(url, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// Merges loader patch into base version JSON (resolves inheritsFrom chain up to 1 level).
    /// </summary>
    public async Task<VersionJson?> MergeInheritsFromAsync(
        VersionJson patch, string versionsDir, CancellationToken ct = default)
    {
        if (patch.InheritsFrom is null) return patch;

        // Try to find the parent locally first.
        var parentPath = Path.Combine(versionsDir, patch.InheritsFrom, $"{patch.InheritsFrom}.json");
        VersionJson? parent = null;

        if (File.Exists(parentPath))
        {
            var json = await File.ReadAllTextAsync(parentPath, ct).ConfigureAwait(false);
            parent = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json, Http.JsonHelper.Options);
        }
        else
        {
            // Try to find from cached manifest.
            var manifest = await GetManifestAsync(false, ct).ConfigureAwait(false);
            var entry = manifest.Versions.FirstOrDefault(v => v.Id == patch.InheritsFrom);
            if (entry is not null)
                parent = await GetVersionJsonAsync(entry.Url, ct).ConfigureAwait(false);
        }

        if (parent is null) return patch;
        return Merge(parent, patch);
    }

    /// <summary>Merges overlay (loader patch) on top of base version JSON.</summary>
    public static VersionJson Merge(VersionJson @base, VersionJson overlay)
    {
        var merged = new VersionJson
        {
            Id          = overlay.Id,
            Type        = overlay.Type,
            MainClass   = string.IsNullOrEmpty(overlay.MainClass) ? @base.MainClass : overlay.MainClass,
            Downloads   = @base.Downloads,
            AssetIndex  = @base.AssetIndex,
            Assets      = @base.Assets,
            JavaVersion = @base.JavaVersion,
            Logging     = @base.Logging,
            ComplianceLevel = @base.ComplianceLevel,
            MinecraftArguments = @base.MinecraftArguments,
        };

        // Merge libraries (overlay appended after base).
        merged.Libraries.AddRange(@base.Libraries);
        merged.Libraries.AddRange(overlay.Libraries);

        // Merge arguments.
        if (@base.Arguments is not null || overlay.Arguments is not null)
        {
            merged.Arguments = new VersionArguments();
            if (@base.Arguments is not null)
            {
                merged.Arguments.Game.AddRange(@base.Arguments.Game);
                merged.Arguments.Jvm.AddRange(@base.Arguments.Jvm);
            }
            if (overlay.Arguments is not null)
            {
                merged.Arguments.Game.AddRange(overlay.Arguments.Game);
                merged.Arguments.Jvm.AddRange(overlay.Arguments.Jvm);
            }
        }
        else if (overlay.MinecraftArguments is not null)
        {
            merged.MinecraftArguments = overlay.MinecraftArguments;
        }

        return merged;
    }

    // ---- Offline seed (shown when network is unavailable) ----
    private static VersionManifest OfflineSeed() => new()
    {
        Latest = new LatestVersions { Release = "1.21.1", Snapshot = "1.21.1" },
        Versions = [
            Seed("1.21.1", "release", "https://piston-meta.mojang.com/v1/packages/a4f2a8e3c3d9c3a5a7b2a8e3c3d9c3a5/1.21.1.json"),
            Seed("1.20.6", "release"),
            Seed("1.20.4", "release"),
            Seed("1.20.1", "release"),
            Seed("1.19.4", "release"),
            Seed("1.19.2", "release"),
            Seed("1.18.2", "release"),
            Seed("1.17.1", "release"),
            Seed("1.16.5", "release"),
            Seed("1.15.2", "release"),
            Seed("1.14.4", "release"),
            Seed("1.12.2", "release"),
            Seed("1.8.9",  "release"),
            Seed("1.7.10", "release"),
        ]
    };

    private static VersionEntry Seed(string id, string type, string url = "")
        => new() { Id = id, Type = type, Url = url, ReleaseTime = DateTime.UtcNow };
}
