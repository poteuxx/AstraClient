using AstraClient.Models;

namespace AstraClient.Services.Launch;

/// <summary>
/// Builds JVM and game argument lists from a resolved VersionJson.
/// Handles both modern (1.13+ arguments.{game,jvm}) and legacy (minecraftArguments) paths.
/// </summary>
public static class ArgumentBuilder
{
    private static readonly Dictionary<string, bool> DefaultFeatures = new()
    {
        ["is_demo_user"]           = false,
        ["has_custom_resolution"]  = false,
    };

    /// <summary>
    /// Builds the complete JVM argument list ready to pass to Process.Start.
    /// </summary>
    public static List<string> BuildJvmArgs(
        VersionJson version,
        string nativesDir,
        string classpath,
        string gameDir,
        string assetsDir,
        string librariesDir, string assetIndexId,
        int memoryMb,
        string? extraJvmArgs = null,
        string? launchVersionName = null)
    {
        var result = new List<string>();

        // Memory.
        result.Add($"-Xmx{memoryMb}M");
        result.Add("-Xms512M");

        var substitutions = BuildSubstitutions(
            launchVersionName ?? version.Id, nativesDir, classpath, gameDir, assetsDir, librariesDir, assetIndexId, "", "", "", "");

        if (version.Arguments?.Jvm is { Count: > 0 } jvmArgs)
        {
            foreach (var el in jvmArgs)
            {
                if (el.IsPlain && el.PlainValue is not null)
                    result.Add(Substitute(el.PlainValue, substitutions));
                else if (el.Conditional is not null && RulesEvaluator.Evaluate(el.Conditional.Rules, DefaultFeatures))
                    result.AddRange(el.Conditional.Values.Select(v => Substitute(v, substitutions)));
            }

            // NeoForge and modern Forge bootstrap profiles use a module path
            // but omit the client classpath. Keep the bootstrap modules while
            // also exposing the resolved Minecraft libraries and client jar.
            if (version.MainClass.Contains("BootstrapLauncher", StringComparison.Ordinal)
                && !result.Contains("-cp", StringComparer.Ordinal))
            {
                result.Add($"-Djava.library.path={nativesDir}");
                result.Add("-cp");
                result.Add(classpath);
            }
        }
        else
        {
            // Minimal legacy JVM args.
            result.Add($"-Djava.library.path={nativesDir}");
            result.Add("-cp");
            result.Add(classpath);
        }

        // Extra user-provided args.
        if (!string.IsNullOrWhiteSpace(extraJvmArgs))
        {
            result.AddRange(extraJvmArgs
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(argument => !argument.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase)
                                && !argument.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    /// <summary>Builds the game argument list.</summary>
    public static List<string> BuildGameArgs(
        VersionJson version,
        string username,
        string uuid,
        string accessToken,
        string gameDir,
        string assetsDir,
        string assetIndexId,
        string versionType = "release",
        string? extraGameArgs = null,
        string? launchVersionName = null)
    {
        var result = new List<string>();

        var substitutions = BuildSubstitutions(
            launchVersionName ?? version.Id, "", "", gameDir, assetsDir, "", assetIndexId,
            username, uuid, accessToken, versionType);

        if (version.Arguments?.Game is { Count: > 0 } gameArgs)
        {
            foreach (var el in gameArgs)
            {
                if (el.IsPlain && el.PlainValue is not null)
                    result.Add(Substitute(el.PlainValue, substitutions));
                else if (el.Conditional is not null && RulesEvaluator.Evaluate(el.Conditional.Rules, DefaultFeatures))
                    result.AddRange(el.Conditional.Values.Select(v => Substitute(v, substitutions)));
            }
        }
        else if (!string.IsNullOrEmpty(version.MinecraftArguments))
        {
            // Legacy flat string — tokenise and substitute.
            var tokens = version.MinecraftArguments.Split(' ');
            result.AddRange(tokens.Select(t => Substitute(t, substitutions)));
        }

        if (!string.IsNullOrWhiteSpace(extraGameArgs))
            result.AddRange(extraGameArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return result;
    }

    private static Dictionary<string, string> BuildSubstitutions(
        string versionId, string nativesDir, string classpath,
        string gameDir, string assetsDir, string librariesDir, string assetIndexId,
        string username, string uuid, string accessToken, string versionType)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${auth_player_name}"]    = username,
            ["${auth_uuid}"]           = uuid,
            ["${auth_access_token}"]   = accessToken,
            ["${auth_session}"]        = accessToken,
            ["${user_type}"]           = "mojang",
            ["${user_properties}"]     = "{}",
            ["${version_name}"]        = versionId,
            ["${version_type}"]        = versionType,
            ["${game_directory}"]      = gameDir,
            ["${assets_root}"]         = assetsDir,
            ["${assets_index_name}"]   = assetIndexId,
            ["${natives_directory}"]   = nativesDir,
            ["${classpath}"]           = classpath,
            ["${launcher_name}"]       = "AstraClient",
            ["${launcher_version}"]    = "1.0.0",
            ["${clientid}"]            = "AstraClient",
            ["${auth_xuid}"]           = "0",
            ["${resolution_width}"]    = "854",
            ["${resolution_height}"]   = "480",
            ["${library_directory}"]   = librariesDir,
            ["${classpath_separator}"] = Path.PathSeparator.ToString(),
        };
    }

    private static string Substitute(string template, Dictionary<string, string> subs)
    {
        foreach (var (key, value) in subs)
            template = template.Replace(key, value);
        return template;
    }
}
