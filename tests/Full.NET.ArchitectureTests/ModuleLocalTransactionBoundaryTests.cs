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
        var sample = discovered.Length > 0
            ? discovered[0]
            : new ModuleBoundaryDebtScanner.CrossModuleTransactionUsage(
                "document",
                "files",
                "src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/HostDocumentItemManagementService.cs",
                "HostDocumentItemManagementService.AddVersionCoreAsync",
                "IHostFileReferenceReader");

        var valid = new[]
        {
            new ModuleBoundaryDebtScanner.CrossModuleTransactionDebt(
                sample.ConsumerModule,
                sample.OwnerModule,
                sample.File,
                sample.EntryPoint,
                sample.ContractType,
                "Temporary cross-module transaction debt for gate testing.",
                "medium",
                "module-data-consistency-boundary-20260807 Task 2"),
        };

        if (discovered.Length > 0)
        {
            Assert.HasCount(0, ModuleBoundaryDebtScanner.ValidateTransactionCatalog(discovered, valid));
        }

        Assert.IsGreaterThan(
            0,
            ModuleBoundaryDebtScanner.ValidateTransactionCatalog(
                discovered.Length > 0 ? discovered : [sample],
                [valid[0] with { EntryPoint = "StaleEntryPoint" }]).Length);
    }
}