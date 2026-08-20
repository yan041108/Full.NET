using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

internal static partial class DomainParameterOwnershipScanner
{
    private static readonly string[] ForbiddenConfigEntryContractTokens =
    [
        "CreateConfigEntryRequest",
        "UpdateConfigEntryRequest",
        "ConfigEntryResponse",
        "ConfigEntryManagementPermissions",
        "ConfigValueKinds",
    ];

    private const string ConfigEntryTableToken = "fn_settings_config_entry";

    public static string[] ScanProductionModuleViolations(string root)
    {
        var modulesRoot = Path.Combine(root, "src", "Modules");
        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutputPath(path))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (IsSettingsModulePath(relativePath)
                || relativePath.Contains(".Contracts/", StringComparison.Ordinal))
            {
                continue;
            }

            violations.AddRange(AnalyzeSource(relativePath, File.ReadAllText(path)));
        }

        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    public static string[] AnalyzeSource(string relativePath, string content)
    {
        if (IsSettingsModulePath(relativePath))
        {
            return [];
        }

        var violations = new List<string>();
        foreach (var token in ForbiddenConfigEntryContractTokens)
        {
            if (content.Contains(token, StringComparison.Ordinal))
            {
                violations.Add(
                    string.Concat(
                        relativePath,
                        ": forbidden ConfigEntry contract token '",
                        token,
                        "'"));
            }
        }

        if (content.Contains(ConfigEntryTableToken, StringComparison.Ordinal))
        {
            violations.Add(
                string.Concat(
                    relativePath,
                    ": forbidden ConfigEntry table token '",
                    ConfigEntryTableToken,
                    "'"));
        }

        if (ParsesBusinessRuleFromConfigEntry(content))
        {
            violations.Add(
                string.Concat(
                    relativePath,
                    ": parses business rules from ConfigEntry values"));
        }

        return violations.ToArray();
    }

    public static bool ContainsForbiddenConfigEntryUsage(string content) =>
        AnalyzeSource("fixture/NegativeModule/Fixture.cs", content).Length > 0;

    public static bool ParsesBusinessRuleFromConfigEntry(string content) =>
        content.Contains("ConfigEntryResponse", StringComparison.Ordinal)
        && ConfigEntryValueAccessRegex().IsMatch(content);

    private static bool IsSettingsModulePath(string relativePath) =>
        relativePath.Contains("/Full.NET.Modules.Settings/", StringComparison.Ordinal);

    private static bool IsBuildOutputPath(string path) =>
        path.Contains(
            string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\bConfigEntryResponse\b[\s\S]{0,200}\.Value\b", RegexOptions.CultureInvariant)]
    private static partial Regex ConfigEntryValueAccessRegex();
}
