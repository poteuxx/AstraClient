namespace AstraClient.Models;

/// <summary>ComboBox-friendly label for a <see cref="LocalServerType"/> value.</summary>
public sealed class ServerTypeOption(LocalServerType type, string label)
{
    public LocalServerType Type { get; } = type;
    public string Label { get; } = label;

    public static IReadOnlyList<ServerTypeOption> All { get; } =
    [
        new(LocalServerType.Vanilla,  "Vanilla"),
        new(LocalServerType.Fabric,   "Fabric"),
        new(LocalServerType.Paper,    "Paper"),
        new(LocalServerType.Forge,    "Forge"),
        new(LocalServerType.NeoForge, "NeoForge"),
    ];
}
