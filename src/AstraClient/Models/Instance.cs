using System.Text.Json.Serialization;

namespace AstraClient.Models;

public class Instance
{
    public string Id       { get; set; } = Guid.NewGuid().ToString("N");
    public string Name     { get; set; } = "New Instance";
    public string GameVersion  { get; set; } = "";
    public string Loader       { get; set; } = "vanilla"; // vanilla|fabric|quilt|neoforge|forge
    public string LoaderVersion { get; set; } = "";
    public int    MemoryMb     { get; set; } = 2048;
    public string JvmArgs      { get; set; } = "";
    public string GameArgs     { get; set; } = "";
    public string? JavaOverride { get; set; } // null = auto
    public DateTime Created    { get; set; } = DateTime.UtcNow;
    public DateTime LastPlayed { get; set; } = DateTime.MinValue;

    /// <summary>Absolute path to the instance game dir (mods/ saves/ etc.).</summary>
    [JsonIgnore]
    public string GameDir => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        ".astraclient", "instances", Id);
}

public class InstalledContent
{
    public string Id        { get; set; } = Guid.NewGuid().ToString("N");
    public string InstanceId { get; set; } = "";
    public string ProjectId  { get; set; } = "";
    public string VersionId  { get; set; } = "";
    public string Name       { get; set; } = "";
    public string FileName   { get; set; } = "";
    public string Type       { get; set; } = "mod"; // mod|resourcepack|shader|datapack
    public string GameVersion { get; set; } = "";
    public bool   Enabled    { get; set; } = true;
    public DateTime Installed { get; set; } = DateTime.UtcNow;
    public long SizeBytes    { get; set; }
}
