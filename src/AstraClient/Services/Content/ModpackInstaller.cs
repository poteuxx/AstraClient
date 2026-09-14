using System.IO.Compression;
using System.Text.Json;
using AstraClient.Models;
using AstraClient.Services.Data;
using AstraClient.Services.Http;
using AstraClient.Services.Modrinth;

namespace AstraClient.Services.Content;

/// <summary>
/// Installs Modrinth .mrpack files from Modrinth, local disk, or share links.
/// </summary>
public sealed class ModpackInstaller
{
    private readonly HttpClientProvider _http;
    private readonly InstanceService _instances;
    private readonly ModrinthService _modrinth;

    public ModpackInstaller(HttpClientProvider http, InstanceService instances, ModrinthService modrinth)
    {
        _http = http;
        _instances = instances;
        _modrinth = modrinth;
    }

    public Task<Instance?> InstallAsync(
        ModrinthHit project,
        ModrinthVersion version,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        var file = version.Files.FirstOrDefault(f => f.Primary && f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                   ?? version.Files.FirstOrDefault(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                   ?? version.Files.FirstOrDefault(f => f.Primary)
                   ?? version.Files.FirstOrDefault();
        if (file is null)
        {
            status?.Report("No modpack file found for this version.");
            return Task.FromResult<Instance?>(null);
        }

        return InstallFromDownloadUrlAsync(
            project.Title,
            file.Url,
            version.GameVersions.FirstOrDefault(),
            status,
            ct);
    }

    public Task<Instance?> InstallFromFileAsync(
        string mrpackPath,
        IProgress<string>? status = null,
        CancellationToken ct = default)
        => InstallFromArchivePathAsync(mrpackPath, displayName: Path.GetFileNameWithoutExtension(mrpackPath), fallbackGameVersion: null, status, ct);

    public async Task<Instance?> InstallFromShareLinkAsync(
        string linkOrCode,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        if (!ModrinthLinkParser.TryParse(linkOrCode, out var parsed))
        {
            status?.Report("Unrecognized share link or code.");
            return null;
        }

        if (!string.Equals(parsed.ProjectType, "modpack", StringComparison.OrdinalIgnoreCase))
        {
            status?.Report("Share links must point to a Modrinth modpack.");
            return null;
        }

        status?.Report($"Resolving {parsed.Slug} on Modrinth…");
        var project = await _modrinth.GetProjectAsync(parsed.Slug, ct).ConfigureAwait(false);
        if (project is null)
        {
            status?.Report("Modpack not found on Modrinth.");
            return null;
        }

        ModrinthVersion? version = null;
        if (!string.IsNullOrEmpty(parsed.VersionId))
        {
            var versions = await _modrinth.GetVersionsAsync(project.Id, ct: ct).ConfigureAwait(false);
            version = versions.FirstOrDefault(v => v.Id == parsed.VersionId || v.VersionNumber == parsed.VersionId)
                      ?? versions.FirstOrDefault(v => v.Name == parsed.VersionId);
        }
        else
        {
            var versions = await _modrinth.GetVersionsAsync(project.Id, ct: ct).ConfigureAwait(false);
            version = versions.FirstOrDefault();
        }

        if (version is null)
        {
            status?.Report("No downloadable version found for this modpack.");
            return null;
        }

        var hit = new ModrinthHit
        {
            ProjectId = project.Id,
            Slug = project.Slug,
            Title = project.Title,
            ProjectType = project.ProjectType
        };
        return await InstallAsync(hit, version, status, ct).ConfigureAwait(false);
    }

    private async Task<Instance?> InstallFromDownloadUrlAsync(
        string title,
        string downloadUrl,
        string? fallbackGameVersion,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var tempMrpack = Path.Combine(Path.GetTempPath(), $"astra-{Guid.NewGuid():N}.mrpack");
        try
        {
            status?.Report($"Downloading {title}…");
            await _http.DownloadFileAsync(downloadUrl, tempMrpack, ct: ct).ConfigureAwait(false);
            return await InstallFromArchivePathAsync(tempMrpack, title, fallbackGameVersion, status, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempMrpack);
        }
    }

    private async Task<Instance?> InstallFromArchivePathAsync(
        string mrpackPath,
        string displayName,
        string? fallbackGameVersion,
        IProgress<string>? status,
        CancellationToken ct)
    {
        try
        {
            status?.Report("Reading modpack index…");
            await using var stream = File.OpenRead(mrpackPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            var index = await ReadIndexAsync(archive, ct).ConfigureAwait(false);
            if (index is null)
            {
                status?.Report("Invalid modpack: modrinth.index.json not found.");
                return null;
            }

            var (gameVersion, loader, loaderVersion) = ResolveDependencies(index);
            if (string.IsNullOrWhiteSpace(gameVersion))
                gameVersion = fallbackGameVersion ?? "1.21.1";

            var instanceName = string.IsNullOrWhiteSpace(index.Name) ? displayName : index.Name;
            status?.Report($"Creating instance {instanceName} ({gameVersion}, {loader})…");
            var instance = await _instances.CreateAsync(instanceName, gameVersion, loader).ConfigureAwait(false);
            instance.LoaderVersion = loaderVersion;
            await _instances.UpdateAsync(instance).ConfigureAwait(false);

            await ApplyPackFilesAsync(archive, index, instance.GameDir, status, ct).ConfigureAwait(false);
            status?.Report("Applying modpack overrides…");
            await ApplyOverridesAsync(archive, instance.GameDir, ct).ConfigureAwait(false);

            _instances.ActiveId = instance.Id;
            status?.Report($"Modpack installed — {instanceName} is now your active instance.");
            return instance;
        }
        catch (Exception ex)
        {
            status?.Report($"Modpack install failed: {ex.Message}");
            return null;
        }
    }

    private async Task ApplyPackFilesAsync(
        ZipArchive archive,
        ModrinthModpackIndex index,
        string gameDir,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var files = index.Files
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .Where(f => f.Env is null || !string.Equals(f.Env.Client, "unsupported", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var total = files.Count;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = files[i];
            var dest = Path.Combine(gameDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (entry.Downloads.Count > 0)
            {
                status?.Report($"Downloading pack files ({i + 1}/{total})…");
                if (!File.Exists(dest))
                    await _http.DownloadFileAsync(entry.Downloads[0], dest, ct: ct).ConfigureAwait(false);
                continue;
            }

            var zipEntry = archive.GetEntry(entry.Path)
                           ?? archive.Entries.FirstOrDefault(e =>
                               string.Equals(e.FullName.TrimEnd('/'), entry.Path, StringComparison.OrdinalIgnoreCase));
            if (zipEntry is null || string.IsNullOrEmpty(zipEntry.Name))
                continue;

            status?.Report($"Extracting pack files ({i + 1}/{total})…");
            await using var src = zipEntry.Open();
            await using var destStream = File.Create(dest);
            await src.CopyToAsync(destStream, ct).ConfigureAwait(false);
        }
    }

    private static async Task<ModrinthModpackIndex?> ReadIndexAsync(ZipArchive archive, CancellationToken ct)
    {
        var entry = archive.GetEntry("modrinth.index.json")
                    ?? archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("modrinth.index.json", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;

        await using var indexStream = entry.Open();
        return await JsonSerializer.DeserializeAsync<ModrinthModpackIndex>(indexStream, JsonHelper.Options, ct).ConfigureAwait(false);
    }

    private static async Task ApplyOverridesAsync(ZipArchive archive, string gameDir, CancellationToken ct)
    {
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name))
                continue;

            var relative = entry.FullName["overrides/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var dest = Path.Combine(gameDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using var src = entry.Open();
            await using var destStream = File.Create(dest);
            await src.CopyToAsync(destStream, ct).ConfigureAwait(false);
        }
    }

    private static (string GameVersion, string Loader, string LoaderVersion) ResolveDependencies(ModrinthModpackIndex index)
    {
        var deps = index.Dependencies;
        var gameVersion = deps.GetValueOrDefault("minecraft") ?? "";

        if (deps.TryGetValue("fabric-loader", out var fabric))
            return (gameVersion, "fabric", fabric);
        if (deps.TryGetValue("quilt-loader", out var quilt))
            return (gameVersion, "quilt", quilt);
        // Modrinth's mrpack spec uses "neoforge" and "forge". Keep the
        // *-loader aliases for packs produced by older Astra versions.
        if (deps.TryGetValue("neoforge", out var neoforge)
            || deps.TryGetValue("neoforge-loader", out neoforge))
            return (gameVersion, "neoforge", neoforge);
        if (deps.TryGetValue("forge", out var forge)
            || deps.TryGetValue("forge-loader", out forge))
            return (gameVersion, "forge", forge);

        return (gameVersion, "vanilla", "");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
