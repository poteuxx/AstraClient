using AstraClient.Models;

namespace AstraClient.Services.Launch;

/// <summary>
/// Evaluates Mojang library and argument rules against the current OS/arch/features.
/// </summary>
public static class RulesEvaluator
{
    private static readonly string OsName = GetOsName();
    private static readonly string OsArch = Environment.Is64BitProcess ? "x86_64" : "x86";

    /// <summary>Returns true if all rules allow inclusion on this platform.</summary>
    public static bool Evaluate(IList<Rule> rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0) return true;

        bool allowed = false;
        foreach (var rule in rules)
        {
            bool matches = MatchesRule(rule, features);
            if (rule.Action == "allow")
                allowed = matches;
            else if (rule.Action == "disallow" && matches)
                return false;
        }
        return allowed;
    }

    private static bool MatchesRule(Rule rule, IReadOnlyDictionary<string, bool>? features)
    {
        if (rule.Os is not null)
        {
            if (rule.Os.Name is not null && !rule.Os.Name.Equals(OsName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (rule.Os.Arch is not null && !rule.Os.Arch.Equals(OsArch, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (rule.Features is not null && features is not null)
        {
            foreach (var (key, required) in rule.Features)
                if (!features.TryGetValue(key, out bool actual) || actual != required)
                    return false;
        }

        return true;
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS())  return "osx";
        return "linux";
    }

    /// <summary>Current OS name string (windows / osx / linux).</summary>
    public static string CurrentOs => OsName;
}
