namespace Full.NET.ArchitectureTests;

internal static partial class ModuleBoundaryDebtScanner
{
    internal sealed record CrossModuleForeignKey(
        string ConsumerModule,
        string OwnerModule,
        string ChildTable,
        string ChildColumn,
        string ReferencedTable,
        string ReferencedColumn,
        string ConstraintName,
        string[] MigrationFiles);

    internal sealed record CrossModuleTransactionUsage(
        string ConsumerModule,
        string OwnerModule,
        string File,
        string EntryPoint,
        string ContractType);

    internal sealed record CrossModuleForeignKeyDebtDocument(CrossModuleForeignKeyDebt[]? Entries);

    internal sealed record CrossModuleForeignKeyDebt(
        string? ConsumerModule,
        string? OwnerModule,
        string? ChildTable,
        string? ChildColumn,
        string? ReferencedTable,
        string? ReferencedColumn,
        string? ConstraintName,
        string[]? MigrationFiles,
        string? EntryPoint,
        string? Reason,
        string? Risk,
        string? RemoveByMilestone);

    internal sealed record CrossModuleTransactionDebtDocument(CrossModuleTransactionDebt[]? Entries);

    internal sealed record CrossModuleTransactionDebt(
        string? ConsumerModule,
        string? OwnerModule,
        string? File,
        string? EntryPoint,
        string? ContractType,
        string? Reason,
        string? Risk,
        string? RemoveByMilestone);

    private sealed record CrossModuleContractBinding(
        string OwnerModule,
        string ContractType,
        string FieldName);
}