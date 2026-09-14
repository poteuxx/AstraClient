using System.Text.Json;
using AstraClient.Models;
using AstraClient.Services.Http;

namespace AstraClient.Services.Modrinth;

public sealed class ModrinthService
{
    private const string BaseUrl = "https://api.modrinth.com/v2";
    private readonly HttpClientProvider _http;

    public ModrinthService(HttpClientProvider http) => _http = http;

    /// <summary>
    /// Search Modrinth with facets. All parameters optional.
    /// </summary>
    public async Task<ModrinthSearchResult?> SearchAsync(
        string query        = "",
        string projectType  = "mod",       // mod | modpack | resourcepack | shader | datapack | plugin
        string? gameVersion = null,
        string? loader      = null,
        string? category    = null,
        string? environment = null,        // client | server
        string sort         = "relevance", // relevance | downloads | follows | newest | updated
        int    limit        = 20,
        int    offset       = 0,
        CancellationToken ct = default)
    {
        var facets = BuildFacets(projectType, gameVersion, loader, category, environment);
        var facetsJson = Uri.EscapeDataString(JsonSerializer.Serialize(facets));
        var q = Uri.EscapeDataString(query);
        var index = sort switch
        {
            "downloads" => "downloads",
            "follows"   => "follows",
            "newest"    => "newest",
            "updated"   => "updated",
            _           => "relevance"
        };

        var url = $"{BaseUrl}/search?limit={limit}&offset={offset}&query={q}&index={index}&facets={facetsJson}";
        return await _http.GetJsonAsync<ModrinthSearchResult>(url, ct).ConfigureAwait(false);
    }

    /// <summary>Fetch a full project by ID or slug.</summary>
    public async Task<ModrinthProject?> GetProjectAsync(string idOrSlug, CancellationToken ct = default)
        => await _http.GetJsonAsync<ModrinthProject>($"{BaseUrl}/project/{idOrSlug}", ct).ConfigureAwait(false);

    /// <summary>Fetch versions for a project, optionally filtered by game version and loader.</summary>
    public async Task<List<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion = null,
        string? loader      = null,
        CancellationToken ct = default)
    {
        var query = $"{BaseUrl}/project/{projectId}/version";
        var parts = new List<string>();
        if (gameVersion is not null) parts.Add($"game_versions=[\"{Uri.EscapeDataString(gameVersion)}\"]");
        if (loader is not null)      parts.Add($"loaders=[\"{loader}\"]");
        if (parts.Count > 0) query += "?" + string.Join("&", parts);

        return await _http.GetJsonAsync<List<ModrinthVersion>>(query, ct).ConfigureAwait(false) ?? [];
    }

    /// <summary>Returns loader tag names available on Modrinth.</summary>
    public async Task<List<string>> GetLoaderTagsAsync(CancellationToken ct = default)
    {
        var tags = await _http.GetJsonAsync<List<ModrinthTag>>($"{BaseUrl}/tag/loader", ct).ConfigureAwait(false);
        return tags?.Select(t => t.Name).ToList() ?? [];
    }

    /// <summary>Returns recent release game version strings from Modrinth tags.</summary>
    public async Task<List<string>> GetGameVersionTagsAsync(CancellationToken ct = default)
    {
        var tags = await _http.GetJsonAsync<List<ModrinthGameVersionTag>>($"{BaseUrl}/tag/game_version", ct).ConfigureAwait(false);
        return tags?
            .Where(t => t.VersionType == "release" && !string.IsNullOrWhiteSpace(t.Version))
            .Select(t => t.Version)
            .Take(50)
            .ToList() ?? [];
    }

    /// <summary>Returns category tag names for a given project type.</summary>
    public async Task<List<string>> GetCategoryTagsAsync(string projectType, CancellationToken ct = default)
    {
        var tags = await _http.GetJsonAsync<List<ModrinthCategoryTag>>($"{BaseUrl}/tag/category", ct).ConfigureAwait(false);
        return tags?
            .Where(t => string.Equals(t.ProjectType, projectType, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    public static string ProjectUrl(ModrinthHit hit)
        => $"https://modrinth.com/{hit.ProjectType}/{hit.Slug}";

    private static List<List<string>> BuildFacets(
        string projectType,
        string? gameVersion,
        string? loader,
        string? category,
        string? environment)
    {
        var facets = new List<List<string>>
        {
            new List<string> { $"project_type:{projectType}" }
        };
        if (!string.IsNullOrEmpty(gameVersion))
            facets.Add(new List<string> { $"versions:{gameVersion}" });
        if (!string.IsNullOrEmpty(loader))
            facets.Add(new List<string> { $"categories:{loader}" });
        if (!string.IsNullOrEmpty(category))
            facets.Add(new List<string> { $"categories:{category}" });
        if (environment == "client")
            facets.Add(new List<string> { "client_side:required" });
        else if (environment == "server")
            facets.Add(new List<string> { "server_side:required" });
        return facets;
    }

    private class ModrinthTag
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private class ModrinthGameVersionTag
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("version_type")]
        public string? VersionType { get; set; }
    }

    private class ModrinthCategoryTag
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("project_type")]
        public string ProjectType { get; set; } = "";
    }
}
