using System.Security.Cryptography;
using System.Text;
using AstraClient.Models;

namespace AstraClient.Services.Auth;

/// <summary>
/// Offline authentication: deterministic UUID v3 (offline Minecraft standard) + no token.
/// Works without any network connection or Microsoft account.
/// </summary>
public sealed class OfflineAuthProvider : IAuthProvider
{
    public bool IsAvailable => true;

    private string _username = "Player";
    public string Username { get => _username; set => _username = value; }

    public Task<UserProfile?> AuthenticateAsync(CancellationToken ct = default)
    {
        var profile = new UserProfile
        {
            Username    = _username,
            Uuid        = GenerateOfflineUuid(_username),
            AccessToken = "0",
            Type        = AuthType.Offline,
        };
        return Task.FromResult<UserProfile?>(profile);
    }

    /// <summary>
    /// Generates the standard offline UUID used by vanilla Minecraft:
    /// UUID v3 of "OfflinePlayer:{username}" (MD5 with version/variant bits set).
    /// </summary>
    public static string GenerateOfflineUuid(string username)
    {
        var input = Encoding.UTF8.GetBytes($"OfflinePlayer:{username}");
        var hash  = MD5.HashData(input);

        // Set version 3 (0011) in bits 12-15 of byte 6.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        // Set variant (10) in bits 6-7 of byte 8.
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return $"{hash[0]:x2}{hash[1]:x2}{hash[2]:x2}{hash[3]:x2}-" +
               $"{hash[4]:x2}{hash[5]:x2}-" +
               $"{hash[6]:x2}{hash[7]:x2}-" +
               $"{hash[8]:x2}{hash[9]:x2}-" +
               $"{hash[10]:x2}{hash[11]:x2}{hash[12]:x2}{hash[13]:x2}{hash[14]:x2}{hash[15]:x2}";
    }
}
