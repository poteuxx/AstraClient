using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Launch;

/// <summary>
/// Resolves library maven coordinates to local disk paths and downloads them.
/// </summary>
public sealed class LibraryResolver
{
    private const string MojangLibraries = "https://libraries.minecraft.net/";
    private readonly HttpClientProvider _http;
    private readonly string _librariesDir;

    public LibraryResolver(HttpClientProvider http, string dataRoot)
    {
        _http = http;
        _librariesDir = Path.Combine(dataRoot, "libraries");
        Directory.CreateDirectory(_librariesDir);
    }

    /// <summary>
    /// Returns the local path for a library artifact, downloading it if absent.
    /// Returns null if the library is excluded by rules or has no download info.
    /// </summary>
    public async Task<string?> ResolveAsync(
        Library lib,
        bool nativesOnly = false,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Rules evaluation.
        if (!RulesEvaluator.Evaluate(lib.Rules)) return null;

        if (nativesOnly)
            return await ResolveNativeAsync(lib, ct).ConfigureAwait(false);

        // Skip native-classifier-only libraries from classpath.
        if (lib.Natives is not null && lib.Downloads?.Artifact is null)
            return null;

        var artifact = lib.Downloads?.Artifact;
        if (artifact is null && lib.Url is null && string.IsNullOrEmpty(lib.Name)) return null;

        string localPath;
        string url;

        if (artifact?.Path is not null)
        {
            localPath = Path.Combine(_librariesDir, artifact.Path.Replace('/', Path.DirectorySeparatorChar));
            url = artifact.Url ?? (MojangLibraries + artifact.Path);
        }
        else
        {
            // Fabric/Quilt-style: name is "group:artifact:version", url is the maven base.
            var mavenPath = MavenCoordToPath(lib.Name);
            localPath = Path.Combine(_librariesDir, mavenPath.Replace('/', Path.DirectorySeparatorChar));
            url = (lib.Url?.TrimEnd('/') ?? MojangLibraries.TrimEnd('/')) + "/" + mavenPath;
        }

        await DownloadIfNeededAsync(url, localPath, artifact?.Sha1, ct).ConfigureAwait(false);
        return File.Exists(localPath) ? localPath : null;
    }

    /// <summary>Resolves and downloads the natives classifier for this platform.</summary>
    public async Task<string?> ResolveNativeAsync(Library lib, CancellationToken ct = default)
    {
        if (lib.Natives is null) return null;
        var osKey = RulesEvaluator.CurrentOs;
        if (!lib.Natives.TryGetValue(osKey, out var classifier)) return null;
        var classifierKey = classifier.Replace("${arch}", Environment.Is64BitProcess ? "64" : "32");

        var native = lib.Downloads?.Classifiers?.GetValueOrDefault(classifierKey);
        if (native?.Url is null) return null;

        var localPath = Path.Combine(_librariesDir,
            (native.Path ?? MavenCoordToPath(lib.Name + ":" + classifierKey))
            .Replace('/', Path.DirectorySeparatorChar));

        await DownloadIfNeededAsync(native.Url, localPath, native.Sha1, ct).ConfigureAwait(false);
        return File.Exists(localPath) ? localPath : null;
    }

    private async Task DownloadIfNeededAsync(string url, string localPath, string? sha1, CancellationToken ct)
    {
        if (File.Exists(localPath))
        {
            if (sha1 is null || VerifySha1(localPath, sha1)) return;
            File.Delete(localPath); // corrupted — re-download
        }

        await _http.DownloadFileAsync(url, localPath, ct: ct).ConfigureAwait(false);
        if (!File.Exists(localPath))
            throw new IOException($"Library download completed without creating {localPath}.");
    }

    /// <summary>Converts "com.example:artifact:1.0" to "com/example/artifact/1.0/artifact-1.0.jar".</summary>
    public static string MavenCoordToPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return name;
        var group   = parts[0].Replace('.', '/');
        var art     = parts[1];
        var ver     = parts[2];
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";
        return $"{group}/{art}/{ver}/{art}-{ver}{classifier}.jar";
    }

    public static bool VerifySha1(string path, string expectedSha1)
    {
        try
        {
            using var fs   = File.OpenRead(path);
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hash = sha1.ComputeHash(fs);
            return Convert.ToHexString(hash).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
