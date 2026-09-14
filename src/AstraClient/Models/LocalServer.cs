namespace AstraClient.Models;

public enum LocalServerType
{
    Vanilla,
    Fabric,
    Paper,
    Forge,
    NeoForge
}

/// <summary>Persisted configuration for one locally managed dedicated server.</summary>
public sealed class LocalServer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Server";
    public string GameVersion { get; set; } = "1.21.1";
    public LocalServerType Type { get; set; } = LocalServerType.Vanilla;
    public int MemoryMb { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public int MaxPlayers { get; set; } = 20;
    public string Motd { get; set; } = "A Minecraft Server";
    public bool OnlineMode { get; set; } = true;
    public bool Pvp { get; set; } = true;
    public bool EulaAccepted { get; set; }
    public string Difficulty { get; set; } = "easy";
    public string GameMode { get; set; } = "survival";
    /// <summary>Required for Forge/NeoForge: the official installer JAR URL for the selected build.</summary>
    public string InstallerUrl { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastStarted { get; set; }
}
