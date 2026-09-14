using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Content;

/// <summary>Exports a local instance as a Modrinth-compatible .mrpack archive.</summary>
public sealed class ModpackExporter
{
    private static readonly string[] ExportRoots =
    [
        "mods",
        "config",
        "defaultconfigs",
        "resourcepacks",
        "shaderpacks",
        "datapacks"
    ];

    public async Task ExportAsync(
        Instance instance,
        string destinationPath,
        string? summary = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        status?.Report("Scanning instance files…");
        var gameDir = instance.GameDir;
        if (!Directory.Exists(gameDir))
            throw new DirectoryNotFoundException("Instance folder not found.");

        var packFiles = new List<(string RelativePath, string AbsolutePath)>();
        foreach (var root in ExportRoots)
        {
            var dir = Path.Combine(gameDir, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(gameDir, file).Replace('\\', '/');
                packFiles.Add((relative, file));
            }
        }

        if (packFiles.Count == 0)
            throw new InvalidOperationException("Nothing to export — add mods, configs or packs to this instance first.");

        var index = new ModrinthModpackIndex
        {
            FormatVersion = 1,
            Game = "minecraft",
            Name = instance.Name,
            VersionId = Guid.NewGuid().ToString("N")[..12],
            Summary = summary ?? $"Exported from Astra — {instance.GameVersion} ({instance.Loader})",
            Dependencies = BuildDependencies(instance)
        };

        status?.Report("Hashing and indexing files…");
        foreach (var (relative, absolute) in packFiles)
        {
            ct.ThrowIfCancellationRequested();
            var hashes = await ComputeHashesAsync(absolute, ct).ConfigureAwait(false);
            index.Files.Add(new ModrinthModpackFile
            {
                Path = relative,
                Downloads = [],
                Hashes = hashes,
                FileSize = new FileInfo(absolute).Length,
                Env = new ModrinthModpackEnv { Client = "required", Server = "optional" }
            });
        }

        var dirName = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dirName)) Directory.CreateDirectory(dirName);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);

        status?.Report("Writing .mrpack archive…");
        await using (var stream = File.Create(destinationPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var indexEntry = archive.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
            await using (var indexStream = indexEntry.Open())
            {
                await JsonSerializer.SerializeAsync(indexStream, index, JsonHelper.Options, ct).ConfigureAwait(false);
            }

            var total = packFiles.Count;
            for (var i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (relative, absolute) = packFiles[i];
                status?.Report($"Packing ({i + 1}/{total})…");
                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                await using var src = File.OpenRead(absolute);
                await using var dest = entry.Open();
                await src.CopyToAsync(dest, ct).ConfigureAwait(false);
            }
        }

        status?.Report($"Exported {packFiles.Count} files → {Path.GetFileName(destinationPath)}");
    }

    private static Dictionary<string, string> BuildDependencies(Instance instance)
    {
        var deps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minecraft"] = instance.GameVersion
        };

        if (!string.IsNullOrWhiteSpace(instance.LoaderVersion))
        {
            switch (instance.Loader.ToLowerInvariant())
            {
                case "fabric": deps["fabric-loader"] = instance.LoaderVersion; break;
                case "quilt": deps["quilt-loader"] = instance.LoaderVersion; break;
                case "forge": deps["forge"] = instance.LoaderVersion; break;
                case "neoforge": deps["neoforge"] = instance.LoaderVersion; break;
            }
        }

        return deps;
    }

    private static async Task<Dictionary<string, string>> ComputeHashesAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var sha1 = await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
        await using var stream512 = File.OpenRead(path);
        var sha512 = await SHA512.HashDataAsync(stream512, ct).ConfigureAwait(false);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sha1"] = Convert.ToHexString(sha1).ToLowerInvariant(),
            ["sha512"] = Convert.ToHexString(sha512).ToLowerInvariant()
        };
    }
}
