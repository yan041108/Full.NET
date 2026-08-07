using System.Text.Json;

namespace Full.NET.ArchitectureTests;

internal static partial class ModuleBoundaryDebtScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string[] ValidateForeignKeyCatalog(
        string root,
        IReadOnlyCollection<CrossModuleForeignKey> discovered,
        IReadOnlyCollection<CrossModuleForeignKeyDebt> catalog)
    {
        var violations = new List<string>(ValidateForeignKeyCatalogEntries(root, catalog));
        var catalogKeys = catalog.Select(ForeignKeyKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in discovered.Where(key => !catalogKeys.Contains(ForeignKeyKey(key))))
        {
            violations.Add(
                string.Concat(
                    "Unregistered cross-module foreign key: ",
                    key.ChildTable, ".", key.ChildColumn,
                    " -> ",
                    key.ReferencedTable, ".", key.ReferencedColumn,
                    " (", key.ConstraintName, ")"));
        }

        var discoveredKeys = discovered.Select(ForeignKeyKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Where(entry => !discoveredKeys.Contains(ForeignKeyKey(entry))))
        {
            violations.Add(
                string.Concat(
                    "Stale cross-module foreign key debt: ",
                    entry.ChildTable, ".", entry.ChildColumn,
                    " -> ",
                    entry.ReferencedTable, ".", entry.ReferencedColumn));
        }

        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    public static string[] ValidateTransactionCatalog(
        IReadOnlyCollection<CrossModuleTransactionUsage> discovered,
        IReadOnlyCollection<CrossModuleTransactionDebt> catalog)
    {
        var violations = new List<string>(ValidateTransactionCatalogEntries(catalog));
        var catalogKeys = catalog.Select(TransactionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var usage in discovered.Where(usage => !catalogKeys.Contains(TransactionKey(usage))))
        {
            violations.Add(
                string.Concat(
                    "Unregistered cross-module transaction contract usage: ",
                    usage.EntryPoint,
                    " (",
                    usage.ConsumerModule, " -> ", usage.OwnerModule,
                    ", ",
                    usage.ContractType,
                    ")"));
        }

        var discoveredKeys = discovered.Select(TransactionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Where(entry => !discoveredKeys.Contains(TransactionKey(entry))))
        {
            violations.Add(string.Concat("Stale cross-module transaction debt: ", entry.EntryPoint));
        }

        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    public static CrossModuleForeignKeyDebt[] LoadForeignKeyCatalog(string root)
    {
        var path = Path.Combine(root, "contracts", "architecture", "module-cross-foreign-key-debt.json");
        if (!File.Exists(path))
        {
            return [];
        }

        var document = JsonSerializer.Deserialize<CrossModuleForeignKeyDebtDocument>(
            File.ReadAllText(path),
            JsonOptions);
        return document?.Entries ?? [];
    }

    public static CrossModuleTransactionDebt[] LoadTransactionCatalog(string root)
    {
        var path = Path.Combine(root, "contracts", "architecture", "module-local-transaction-debt.json");
        if (!File.Exists(path))
        {
            return [];
        }

        var document = JsonSerializer.Deserialize<CrossModuleTransactionDebtDocument>(
            File.ReadAllText(path),
            JsonOptions);
        return document?.Entries ?? [];
    }

    private static string[] ValidateForeignKeyCatalogEntries(
        string root,
        IReadOnlyCollection<CrossModuleForeignKeyDebt> catalog)
    {
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
        {
            if (ContainsWildcard(entry.ConsumerModule)
                || ContainsWildcard(entry.OwnerModule)
                || ContainsWildcard(entry.ChildTable)
                || ContainsWildcard(entry.ChildColumn)
                || ContainsWildcard(entry.ReferencedTable)
                || ContainsWildcard(entry.ReferencedColumn)
                || ContainsWildcard(entry.ConstraintName)
                || ContainsWildcard(entry.EntryPoint))
            {
                violations.Add(string.Concat("Foreign-key debt contains wildcard: ", entry.ConstraintName));
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Reason)
                || string.IsNullOrWhiteSpace(entry.Risk)
                || string.IsNullOrWhiteSpace(entry.RemoveByMilestone))
            {
                violations.Add(string.Concat("Foreign-key debt lacks reason/risk/milestone: ", entry.ConstraintName));
                continue;
            }

            if (entry.MigrationFiles is null || entry.MigrationFiles.Length == 0)
            {
                violations.Add(string.Concat("Foreign-key debt lacks migration files: ", entry.ConstraintName));
                continue;
            }

            foreach (var migrationFile in entry.MigrationFiles)
            {
                if (ContainsWildcard(migrationFile)
                    || !File.Exists(Path.Combine(
                        root,
                        migrationFile.Replace('/', Path.DirectorySeparatorChar))))
                {
                    violations.Add(string.Concat("Foreign-key debt migration file missing: ", migrationFile));
                }
            }

            if (!seen.Add(ForeignKeyKey(entry)))
            {
                violations.Add(string.Concat("Duplicate foreign-key debt: ", entry.ConstraintName));
            }
        }

        return violations.ToArray();
    }

    private static string[] ValidateTransactionCatalogEntries(
        IReadOnlyCollection<CrossModuleTransactionDebt> catalog)
    {
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
        {
            if (ContainsWildcard(entry.ConsumerModule)
                || ContainsWildcard(entry.OwnerModule)
                || ContainsWildcard(entry.File)
                || ContainsWildcard(entry.EntryPoint)
                || ContainsWildcard(entry.ContractType))
            {
                violations.Add(string.Concat("Transaction debt contains wildcard: ", entry.EntryPoint));
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Reason)
                || string.IsNullOrWhiteSpace(entry.Risk)
                || string.IsNullOrWhiteSpace(entry.RemoveByMilestone))
            {
                violations.Add(string.Concat("Transaction debt lacks reason/risk/milestone: ", entry.EntryPoint));
                continue;
            }

            if (!seen.Add(TransactionKey(entry)))
            {
                violations.Add(string.Concat("Duplicate transaction debt: ", entry.EntryPoint));
            }
        }

        return violations.ToArray();
    }

    private static bool ContainsWildcard(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains('*', StringComparison.Ordinal)
            || value.Contains('?', StringComparison.Ordinal));
}