using System.Text.RegularExpressions;

namespace AstraClient.Services.Modrinth;

/// <summary>Parses Modrinth project URLs and compact share codes for modpack import.</summary>
public static partial class ModrinthLinkParser
{
    public sealed record ParsedLink(string ProjectType, string Slug, string? VersionId);

    /// <summary>
    /// Accepts full URLs, slugs, or Astra codes like <c>modrinth:modpack:fabulously-optimized</c>.
    /// </summary>
    public static bool TryParse(string? input, out ParsedLink link)
    {
        link = null!;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var text = input.Trim();

        if (text.StartsWith("modrinth:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
            {
                link = new ParsedLink(parts[1].ToLowerInvariant(), parts[2], parts.Length >= 4 ? parts[3] : null);
                return true;
            }
        }

        var match = ModrinthUrlRegex().Match(text);
        if (match.Success)
        {
            link = new ParsedLink(
                match.Groups["type"].Value.ToLowerInvariant(),
                match.Groups["slug"].Value,
                match.Groups["version"].Success ? match.Groups["version"].Value : null);
            return true;
        }

        if (SlugOnlyRegex().IsMatch(text))
        {
            link = new ParsedLink("modpack", text, null);
            return true;
        }

        return false;
    }

    public static string ToShareUrl(string projectType, string slug, string? versionId = null)
        => string.IsNullOrEmpty(versionId)
            ? $"https://modrinth.com/{projectType}/{slug}"
            : $"https://modrinth.com/{projectType}/{slug}/version/{versionId}";

    public static string ToShareCode(string projectType, string slug, string? versionId = null)
        => string.IsNullOrEmpty(versionId)
            ? $"modrinth:{projectType}:{slug}"
            : $"modrinth:{projectType}:{slug}:{versionId}";

    [GeneratedRegex(@"modrinth\.com/(?<type>modpack|mod|plugin|resourcepack|shader|datapack)/(?<slug>[a-z0-9._-]+)(?:/version/(?<version>[a-zA-Z0-9._-]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex ModrinthUrlRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex SlugOnlyRegex();
}
