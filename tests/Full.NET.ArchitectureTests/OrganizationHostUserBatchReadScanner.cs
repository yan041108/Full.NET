using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

internal static partial class OrganizationHostUserBatchReadScanner
{
    public static string[] ScanOrganizationListCompositionViolations(string root)
    {
        var modulesRoot = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Organization");
        if (!Directory.Exists(modulesRoot))
        {
            return ["Organization module root was not found."];
        }

        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(modulesRoot, "*QueryService.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutputPath(path))
            {
                continue;
            }

            var content = File.ReadAllText(path);
            if (!content.Contains("IHostUserDisplayDirectory", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            var listBody = ExtractMethodBody(content, "ListAsync");
            if (string.IsNullOrWhiteSpace(listBody))
            {
                violations.Add(string.Concat("Missing ListAsync body in ", relativePath));
                continue;
            }

            if (ContainsPerRowActiveUserLookup(listBody))
            {
                violations.Add(
                    string.Concat(
                        "Per-row host user lookup in ListAsync: ",
                        relativePath));
                continue;
            }

            if (!listBody.Contains("FindHostUsersAsync", StringComparison.Ordinal))
            {
                violations.Add(
                    string.Concat(
                        "ListAsync must batch host users via FindHostUsersAsync: ",
                        relativePath));
            }
        }

        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    public static bool ContainsPerRowActiveUserLookup(string methodBody) =>
        methodBody.Contains("FindActiveHostUserAsync", StringComparison.Ordinal)
        || PerRowLookupRegex().IsMatch(methodBody);

    private static string ExtractMethodBody(string content, string methodName)
    {
        var match = MethodDeclarationRegex(methodName).Match(content);
        if (!match.Success)
        {
            return string.Empty;
        }

        var braceIndex = content.IndexOf('{', match.Index + match.Length);
        if (braceIndex < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = braceIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[braceIndex..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static bool IsBuildOutputPath(string path) =>
        path.Contains(
            string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static Regex MethodDeclarationRegex(string methodName) =>
        new(
            "(?:^|[\\r\\n])\\s*public\\s+async\\s+[\\w<>,\\.\\[\\]\\?\\s]+\\b"
            + Regex.Escape(methodName)
            + "\\s*\\(",
            RegexOptions.CultureInvariant);

    [GeneratedRegex(
        @"foreach\s*\([^)]+\)\s*\{[^}]*FindActiveHostUserAsync",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PerRowLookupRegex();
}