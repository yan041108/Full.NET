namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class ModuleLocalTransactionBoundaryTests
{
    [TestMethod]
    public void Transaction_gate_matches_catalog()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var discovered = ModuleBoundaryDebtScanner.ScanCrossModuleTransactionUsages(root);
        var catalog = ModuleBoundaryDebtScanner.LoadTransactionCatalog(root);
        var violations = ModuleBoundaryDebtScanner.ValidateTransactionCatalog(discovered, catalog);

        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Transaction_gate_rejects_stale_entries()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var discovered = ModuleBoundaryDebtScanner.ScanCrossModuleTransactionUsages(root);
        Assert.IsGreaterThan(0, discovered.Length);

        var valid = discovered.Select(usage => new ModuleBoundaryDebtScanner.CrossModuleTransactionDebt(
            usage.ConsumerModule,
            usage.OwnerModule,
            usage.File,
            usage.EntryPoint,
            usage.ContractType,
            "Temporary cross-module transaction debt for gate testing.",
            "medium",
            "module-data-consistency-boundary-20260807 Task 2")).ToArray();

        Assert.HasCount(0, ModuleBoundaryDebtScanner.ValidateTransactionCatalog(discovered, valid));
        Assert.IsGreaterThan(
            0,
            ModuleBoundaryDebtScanner.ValidateTransactionCatalog(
                discovered,
                [valid[0] with { EntryPoint = "StaleEntryPoint" }]).Length);
    }
}