using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

internal static partial class ModuleBoundaryDebtScanner
{
    public static CrossModuleForeignKey[] ScanCrossModuleForeignKeys(string root)
    {
        var migrationsRoot = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Migrations.DbUp",
            "Migrations");
        var keys = new Dictionary<string, CrossModuleForeignKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in new[] { "SqlServer", "MySql" })
        {
            var providerRoot = Path.Combine(migrationsRoot, provider);
            if (!Directory.Exists(providerRoot))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(providerRoot, "*.sql"))
            {
                var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
                var content = File.ReadAllText(path);
                var childTables = ExtractChildTables(content);
                foreach (Match match in ForeignKeyRegex().Matches(content))
                {
                    var childTable = ResolveChildTable(content, match.Index, childTables);
                    if (string.IsNullOrWhiteSpace(childTable))
                    {
                        continue;
                    }

                    var referencedTable = match.Groups["refTable"].Value;
                    var consumerModule = ExtractTableOwner(childTable);
                    var ownerModule = ExtractTableOwner(referencedTable);
                    if (string.Equals(consumerModule, ownerModule, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var discovered = new CrossModuleForeignKey(
                        consumerModule,
                        ownerModule,
                        childTable,
                        match.Groups["childColumn"].Value,
                        referencedTable,
                        match.Groups["refColumn"].Value,
                        match.Groups["constraint"].Value,
                        [relativePath]);
                    var key = ForeignKeyKey(discovered);
                    if (keys.TryGetValue(key, out var existing))
                    {
                        var files = existing.MigrationFiles
                            .Concat(discovered.MigrationFiles)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(file => file, StringComparer.Ordinal)
                            .ToArray();
                        keys[key] = existing with { MigrationFiles = files };
                    }
                    else
                    {
                        keys[key] = discovered;
                    }
                }
            }
        }

        return keys.Values
            .OrderBy(key => key.ChildTable, StringComparer.Ordinal)
            .ThenBy(key => key.ConstraintName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ExtractChildTables(string content)
    {
        var tables = new List<string>();
        foreach (Match match in CreateTableRegex().Matches(content))
        {
            tables.Add(match.Groups["table"].Value);
        }

        foreach (Match match in AlterTableRegex().Matches(content))
        {
            tables.Add(match.Groups["table"].Value);
        }

        return tables.ToArray();
    }

    private static string ResolveChildTable(
        string content,
        int foreignKeyIndex,
        IReadOnlyList<string> childTables)
    {
        if (childTables.Count == 0)
        {
            return string.Empty;
        }

        var positions = new List<(int Index, string Table)>();
        foreach (Match match in CreateTableRegex().Matches(content))
        {
            positions.Add((match.Index, match.Groups["table"].Value));
        }

        foreach (Match match in AlterTableRegex().Matches(content))
        {
            positions.Add((match.Index, match.Groups["table"].Value));
        }

        return positions
            .Where(position => position.Index <= foreignKeyIndex)
            .OrderByDescending(position => position.Index)
            .Select(position => position.Table)
            .FirstOrDefault() ?? childTables[0];
    }

    private static string ExtractTableOwner(string tableName)
    {
        var match = TableOwnerRegex().Match(tableName);
        return match.Success ? match.Groups["owner"].Value.ToLowerInvariant() : string.Empty;
    }

    private static string ForeignKeyKey(CrossModuleForeignKey key) =>
        string.Join('|', key.ChildTable, key.ChildColumn, key.ReferencedTable, key.ConstraintName);

    private static string ForeignKeyKey(CrossModuleForeignKeyDebt entry) =>
        string.Join('|', entry.ChildTable, entry.ChildColumn, entry.ReferencedTable, entry.ConstraintName);

    [GeneratedRegex(
        @"CONSTRAINT\s+(?<constraint>FK_[A-Za-z0-9_]+)\s+FOREIGN\s+KEY\s*\(\s*(?<childColumn>[A-Za-z0-9_]+)\s*\)\s+REFERENCES\s+(?:dbo\.)?(?<refTable>fn_[a-z0-9_]+)\s*\(\s*(?<refColumn>[A-Za-z0-9_]+)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForeignKeyRegex();

    [GeneratedRegex(
        @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:dbo\.)?(?<table>fn_[a-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateTableRegex();

    [GeneratedRegex(
        @"ALTER\s+TABLE\s+(?:dbo\.)?(?<table>fn_[a-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlterTableRegex();

    [GeneratedRegex(@"^fn_(?<owner>[a-z0-9]+)_", RegexOptions.CultureInvariant)]
    private static partial Regex TableOwnerRegex();
}