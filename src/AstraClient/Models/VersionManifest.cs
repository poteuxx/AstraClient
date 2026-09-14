using System.Text.Json.Serialization;

namespace AstraClient.Models;

// ---- Manifest ----------------------------------------------------------------

public class VersionManifest
{
    [JsonPropertyName("latest")]
    public LatestVersions? Latest { get; set; }

    [JsonPropertyName("versions")]
    public List<VersionEntry> Versions { get; set; } = [];
}

public class LatestVersions
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = "";

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = "";
}

public class VersionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "release"; // release | snapshot | old_beta | old_alpha

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTime ReleaseTime { get; set; }

    [JsonPropertyName("sha1")]
    public string Sha1 { get; set; } = "";

    [JsonPropertyName("complianceLevel")]
    public int ComplianceLevel { get; set; }
}
