using System.Text.Json;
using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed partial class ModuleTableOwnershipTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void Production_module_table_access_is_owned_or_exactly_registered()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var accesses = ScanProductionAccesses(root);
        var debtPath = Path.Combine(
            root,
            "contracts",
            "architecture",
            "module-table-access-debt.json");
        var debt = File.Exists(debtPath)
            ? JsonSerializer.Deserialize<DebtDocument>(
                File.ReadAllText(debtPath),
                JsonOptions)?.Entries ?? []
            : [];

        var violations = Analyze(accesses, debt);

        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Ownership_gate_rejects_new_access_and_accepts_only_exact_debt()
    {
        var access = new TableAccess(
            "alpha",
            "fn_beta_widget",
            "src/Modules/Full.NET.Modules.Alpha/Persistence/AlphaSql.cs");
        var exactDebt = new TableAccessDebt(
            access.SourceModule,
            access.Table,
            access.File,
            "迁移到 Beta 提供的模块契约。",
            "Full.NET 1.1");

        Assert.HasCount(1, Analyze([access], []));
        Assert.HasCount(0, Analyze([access], [exactDebt]));
        Assert.HasCount(
            2,
            Analyze(
                [access],
                [exactDebt with { File = "src/Modules/Full.NET.Modules.Alpha/**/*.cs" }]));
        Assert.HasCount(
            2,
            Analyze(
                [access],
                [exactDebt with { SourceModule = null }]));
    }

    private static TableAccess[] ScanProductionAccesses(string root)
    {
        var modulesRoot = Path.Combine(root, "src", "Modules");
        return Directory
            .EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .SelectMany(path => ScanFile(root, path))
            .Distinct()
            .OrderBy(access => access.SourceModule, StringComparer.Ordinal)
            .ThenBy(access => access.Table, StringComparer.Ordinal)
            .ThenBy(access => access.File, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<TableAccess> ScanFile(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
        var moduleMatch = ModuleDirectoryRegex().Match(relativePath);
        if (!moduleMatch.Success)
        {
            yield break;
        }

        var sourceModule = moduleMatch.Groups["module"].Value.ToLowerInvariant();
        foreach (Match match in TableNameRegex().Matches(File.ReadAllText(path)))
        {
            var table = match.Value;
            var owner = TableOwnerRegex().Match(table).Groups["owner"].Value;
            if (!string.Equals(sourceModule, owner, StringComparison.OrdinalIgnoreCase))
            {
                yield return new TableAccess(sourceModule, table, relativePath);
            }
        }
    }

    private static string[] Analyze(
        IReadOnlyCollection<TableAccess> accesses,
        IReadOnlyCollection<TableAccessDebt> debt)
    {
        var violations = new List<string>();
        var debtKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acceptedDebt = new List<TableAccessDebt>();
        foreach (var entry in debt)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceModule)
                || string.IsNullOrWhiteSpace(entry.Table)
                || string.IsNullOrWhiteSpace(entry.File))
            {
                violations.Add($"Debt entry lacks exact identity fields: {Format(entry)}");
                continue;
            }

            if (ContainsWildcard(entry.SourceModule)
                || ContainsWildcard(entry.Table)
                || ContainsWildcard(entry.File))
            {
                violations.Add($"Debt entry contains wildcard: {Format(entry)}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Reason)
                || string.IsNullOrWhiteSpace(entry.RemovalMilestone))
            {
                violations.Add($"Debt entry lacks reason or removal milestone: {Format(entry)}");
                continue;
            }

            if (!debtKeys.Add(Key(entry.SourceModule, entry.Table, entry.File)))
            {
                violations.Add($"Duplicate debt entry: {Format(entry)}");
                continue;
            }

            acceptedDebt.Add(entry);
        }

        foreach (var access in accesses)
        {
            if (!debtKeys.Contains(Key(access.SourceModule, access.Table, access.File)))
            {
                violations.Add($"Unregistered cross-module table access: {Format(access)}");
            }
        }

        var accessKeys = accesses
            .Select(access => Key(access.SourceModule, access.Table, access.File))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in acceptedDebt.Where(entry =>
                     !accessKeys.Contains(Key(
                         entry.SourceModule!,
                         entry.Table!,
                         entry.File!))))
        {
            violations.Add($"Stale debt entry: {Format(entry)}");
        }

        return violations
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsWildcard(string value) =>
        value.Contains('*', StringComparison.Ordinal)
        || value.Contains('?', StringComparison.Ordinal);

    private static string Key(string sourceModule, string table, string file) =>
        $"{sourceModule}|{table}|{file.Replace('\\', '/')}";

    private static string Format(TableAccess access) =>
        $"{access.SourceModule} -> {access.Table} @ {access.File}";

    private static string Format(TableAccessDebt entry) =>
        $"{entry.SourceModule ?? "<missing>"} -> {entry.Table ?? "<missing>"}"
        + $" @ {entry.File ?? "<missing>"}";

    private static bool IsBuildOutputPath(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?:^|/)Full\.NET\.Modules\.(?<module>[^/]+)(?:/|$)")]
    private static partial Regex ModuleDirectoryRegex();

    [GeneratedRegex(@"\bfn_[a-z0-9]+_[a-z0-9_]+\b")]
    private static partial Regex TableNameRegex();

    [GeneratedRegex(@"^fn_(?<owner>[a-z0-9]+)_")]
    private static partial Regex TableOwnerRegex();

    private sealed record DebtDocument(TableAccessDebt[] Entries);

    private sealed record TableAccess(string SourceModule, string Table, string File);

    private sealed record TableAccessDebt(
        string? SourceModule,
        string? Table,
        string? File,
        string? Reason,
        string? RemovalMilestone);
}
