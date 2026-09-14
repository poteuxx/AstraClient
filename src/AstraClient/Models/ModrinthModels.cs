using System.Text.Json.Serialization;

namespace AstraClient.Models;

// ---- Modrinth search result --------------------------------------------------

public class ModrinthSearchResult
{
    [JsonPropertyName("hits")]
    public List<ModrinthHit> Hits { get; set; } = [];

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}

public class ModrinthHit
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("project_type")]
    public string ProjectType { get; set; } = "mod";

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = [];

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("follows")]
    public int Follows { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("color")]
    public int? Color { get; set; }

    [JsonPropertyName("date_created")]
    public DateTime DateCreated { get; set; }

    [JsonPropertyName("date_modified")]
    public DateTime DateModified { get; set; }

    [JsonPropertyName("client_side")]
    public string ClientSide { get; set; } = "unknown";

    [JsonPropertyName("server_side")]
    public string ServerSide { get; set; } = "unknown";

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("gallery")]
    public List<string> Gallery { get; set; } = [];

    // Runtime: not from JSON
    [JsonIgnore]
    public bool IsFavorite { get; set; }
}

// ---- Full project ------------------------------------------------------------

public class ModrinthProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("project_type")]
    public string ProjectType { get; set; } = "mod";

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("versions")]
    public List<string> VersionIds { get; set; } = [];

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("follows")]
    public int Follows { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("color")]
    public int? Color { get; set; }

    [JsonPropertyName("published")]
    public DateTime Published { get; set; }

    [JsonPropertyName("updated")]
    public DateTime Updated { get; set; }

    [JsonPropertyName("client_side")]
    public string ClientSide { get; set; } = "unknown";

    [JsonPropertyName("server_side")]
    public string ServerSide { get; set; } = "unknown";

    [JsonPropertyName("license")]
    public ModrinthLicense? License { get; set; }

    [JsonPropertyName("gallery")]
    public List<ModrinthGalleryImage> Gallery { get; set; } = [];

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("issues_url")]
    public string? IssuesUrl { get; set; }

    [JsonPropertyName("wiki_url")]
    public string? WikiUrl { get; set; }

    [JsonPropertyName("discord_url")]
    public string? DiscordUrl { get; set; }
}

public class ModrinthLicense
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class ModrinthGalleryImage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

// ---- Version -----------------------------------------------------------------

public class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = "";

    [JsonPropertyName("version_type")]
    public string VersionType { get; set; } = "release"; // release | beta | alpha

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("files")]
    public List<ModrinthFile> Files { get; set; } = [];

    [JsonPropertyName("date_published")]
    public DateTime DatePublished { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("dependencies")]
    public List<ModrinthDependency> Dependencies { get; set; } = [];

    public override string ToString() => Name;
}

public class ModrinthFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("hashes")]
    public Dictionary<string, string>? Hashes { get; set; }
}

public class ModrinthDependency
{
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = "optional";
}

// ---- Modpack (.mrpack) index -------------------------------------------------

public class ModrinthModpackIndex
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("game")]
    public string Game { get; set; } = "minecraft";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = "";

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("files")]
    public List<ModrinthModpackFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ModrinthModpackFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("downloads")]
    public List<string> Downloads { get; set; } = [];

    [JsonPropertyName("hashes")]
    public Dictionary<string, string>? Hashes { get; set; }

    [JsonPropertyName("fileSize")]
    public long? FileSize { get; set; }

    [JsonPropertyName("env")]
    public ModrinthModpackEnv? Env { get; set; }
}

public class ModrinthModpackEnv
{
    [JsonPropertyName("client")]
    public string Client { get; set; } = "optional";

    [JsonPropertyName("server")]
    public string Server { get; set; } = "optional";
}
