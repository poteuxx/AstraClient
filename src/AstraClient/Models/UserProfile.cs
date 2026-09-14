namespace AstraClient.Models;

public enum AuthType
{
    Offline,
    Microsoft
}

/// <summary>A Minecraft player profile used for launching (online or offline).</summary>
public class UserProfile
{
    public string Username { get; set; } = "Player";
    public string Uuid { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? Xuid { get; set; }
    public AuthType Type { get; set; } = AuthType.Offline;

    /// <summary>Display name for the account area.</summary>
    public string DisplayName => Username;

    public bool IsOnline => Type == AuthType.Microsoft;

    /// <summary>Two-character initials for the avatar fallback.</summary>
    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Username)) return "?";
            var parts = Username.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return (parts[0][0] + "" + parts[1][0]).ToUpperInvariant();
            return Username.Substring(0, Math.Min(2, Username.Length)).ToUpperInvariant();
        }
    }
}
