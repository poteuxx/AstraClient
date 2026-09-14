using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraClient.Models;

// ---- Full Minecraft version JSON (both legacy + modern) ----------------------

public class VersionJson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "release";

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("mainClass")]
    public string MainClass { get; set; } = "";

    /// <summary>Modern args (1.13+).</summary>
    [JsonPropertyName("arguments")]
    public VersionArguments? Arguments { get; set; }

    /// <summary>Legacy flat string (≤ 1.12).</summary>
    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = [];

    [JsonPropertyName("downloads")]
    public VersionDownloads? Downloads { get; set; }

    [JsonPropertyName("assetIndex")]
    public AssetIndex? AssetIndex { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionInfo? JavaVersion { get; set; }

    [JsonPropertyName("logging")]
    public JsonElement? Logging { get; set; }

    [JsonPropertyName("complianceLevel")]
    public int ComplianceLevel { get; set; }

    [JsonPropertyName("minimumLauncherVersion")]
    public int MinimumLauncherVersion { get; set; }
}

// ---- Arguments ---------------------------------------------------------------

public class VersionArguments
{
    [JsonPropertyName("game")]
    public List<ArgumentElement> Game { get; set; } = [];

    [JsonPropertyName("jvm")]
    public List<ArgumentElement> Jvm { get; set; } = [];
}

/// <summary>
/// An argument element is either a plain string or a conditional object
/// { "rules": [...], "value": "string"|["array"] }.
/// We use a custom converter so both forms can coexist in one list.
/// </summary>
[JsonConverter(typeof(ArgumentElementConverter))]
public class ArgumentElement
{
    public string? PlainValue { get; set; }
    public ConditionalArgument? Conditional { get; set; }
    public bool IsPlain => PlainValue is not null;
}

public class ConditionalArgument
{
    [JsonPropertyName("rules")]
    public List<Rule> Rules { get; set; } = [];

    // value can be string or string[]
    public List<string> Values { get; set; } = [];
}

public class ArgumentElementConverter : JsonConverter<ArgumentElement>
{
    public override ArgumentElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new ArgumentElement { PlainValue = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var cond = new ConditionalArgument();
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.TryGetProperty("rules", out var rulesEl))
                cond.Rules = JsonSerializer.Deserialize<List<Rule>>(rulesEl.GetRawText(), options) ?? [];
            if (root.TryGetProperty("value", out var valEl))
            {
                if (valEl.ValueKind == JsonValueKind.String)
                    cond.Values.Add(valEl.GetString()!);
                else if (valEl.ValueKind == JsonValueKind.Array)
                    foreach (var item in valEl.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String)
                            cond.Values.Add(item.GetString()!);
            }
            return new ArgumentElement { Conditional = cond };
        }

        reader.Skip();
        return new ArgumentElement { PlainValue = string.Empty };
    }

    public override void Write(Utf8JsonWriter writer, ArgumentElement value, JsonSerializerOptions options)
        => throw new NotSupportedException();
}

// ---- Libraries ---------------------------------------------------------------

public class Library
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("rules")]
    public List<Rule> Rules { get; set; } = [];

    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("extract")]
    public ExtractInfo? Extract { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; } // fabric-style direct URL
}

public class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public LibraryArtifact? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, LibraryArtifact>? Classifiers { get; set; }
}

public class LibraryArtifact
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class ExtractInfo
{
    [JsonPropertyName("exclude")]
    public List<string> Exclude { get; set; } = [];
}

// ---- Rules -------------------------------------------------------------------

public class Rule
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "allow"; // allow | disallow

    [JsonPropertyName("os")]
    public OsRule? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public class OsRule
{
    [JsonPropertyName("name")]
    public string? Name { get; set; } // windows | linux | osx

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

// ---- Downloads ---------------------------------------------------------------

public class VersionDownloads
{
    [JsonPropertyName("client")]
    public DownloadInfo? Client { get; set; }

    [JsonPropertyName("server")]
    public DownloadInfo? Server { get; set; }

    [JsonPropertyName("client_mappings")]
    public DownloadInfo? ClientMappings { get; set; }
}

public class DownloadInfo
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

// ---- Asset index -------------------------------------------------------------

public class AssetIndex
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }
}

public class AssetIndexObjects
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject> Objects { get; set; } = [];
}

public class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

// ---- Java version ------------------------------------------------------------

public class JavaVersionInfo
{
    [JsonPropertyName("component")]
    public string Component { get; set; } = "jre-legacy";

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; } = 8;
}
