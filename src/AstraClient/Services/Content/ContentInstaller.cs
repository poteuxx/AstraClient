using AstraClient.Models;
using AstraClient.Services.Data;
using AstraClient.Services.Http;

namespace AstraClient.Services.Content;

/// <summary>
/// Downloads a Modrinth project file into the correct subfolder of the active instance
/// and records it in the library.
/// </summary>
public sealed class ContentInstaller
{
    private readonly HttpClientProvider _http;
    private readonly InstanceService    _instances;
    private readonly LibraryService     _library;

    public ContentInstaller(HttpClientProvider http, InstanceService instances, LibraryService library)
    {
        _http      = http;
        _instances = instances;
        _library   = library;
    }

    /// <summary>
    /// Downloads the primary file from a Modrinth version into the active instance's folder,
    /// then records it in the library.
    /// Returns the InstalledContent record, or null on failure.
    /// </summary>
    public async Task<InstalledContent?> InstallAsync(
        ModrinthHit project,
        ModrinthVersion version,
        Instance? targetInstance = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        var instance = targetInstance ?? _instances.Active;
        if (instance is null)
        {
            status?.Report("No active instance selected. Create one in Manage first.");
            return null;
        }

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file is null)
        {
            status?.Report("No downloadable file found for this version.");
            return null;
        }

        var subFolder = ContentTypeToFolder(project.ProjectType);
        var folder    = Path.Combine(instance.GameDir, subFolder);
        Directory.CreateDirectory(folder);
        var destPath  = Path.Combine(folder, file.Filename);

        status?.Report($"Installing {project.Title} → {subFolder}/{file.Filename}…");

        if (!File.Exists(destPath))
        {
            try
            {
                await _http.DownloadFileAsync(file.Url, destPath, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                status?.Report($"Download failed: {ex.Message}");
                return null;
            }
        }

        var record = new InstalledContent
        {
            InstanceId  = instance.Id,
            ProjectId   = project.ProjectId,
            VersionId   = version.Id,
            Name        = project.Title,
            FileName    = file.Filename,
            Type        = project.ProjectType,
            GameVersion = version.GameVersions.FirstOrDefault() ?? instance.GameVersion,
            SizeBytes   = file.Size,
        };

        await _library.AddAsync(record).ConfigureAwait(false);
        status?.Report($"Installed {project.Title} successfully.");
        return record;
    }

    /// <summary>
    /// Removes an installed content item from disk and the library.
    /// </summary>
    public async Task UninstallAsync(InstalledContent content, Instance instance, CancellationToken ct = default)
    {
        var subFolder = ContentTypeToFolder(content.Type);
        var path      = Path.Combine(instance.GameDir, subFolder, content.FileName);
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (Exception ex) { Console.Error.WriteLine($"[ContentInstaller] Delete failed: {ex.Message}"); }
        }
        await _library.RemoveAsync(content.InstanceId, content.Id).ConfigureAwait(false);
    }

    private static string ContentTypeToFolder(string projectType) => projectType switch
    {
        "resourcepack" => "resourcepacks",
        "shader"       => "shaderpacks",
        "datapack"     => "datapacks",
        "plugin"       => "plugins",
        "modpack"      => "modpacks",
        _              => "mods",
    };
}
