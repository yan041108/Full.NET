namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class ModuleLocalTransactionBoundaryTests
{
    [TestMethod]
    public void Contract_definition_scanner_maps_contract_project_to_owning_module()
    {
        var definitions = ModuleBoundaryDebtScanner.ScanContractDefinitions(
            ArchitectureRepositoryRoot.Find());

        Assert.AreEqual("files", definitions["IHostFileReferenceClaimService"]);
        Assert.AreEqual("identity", definitions["IHostUserDirectory"]);
    }

    /// <summary>验证 Port 实现扫描器不会把跨模块构造函数消费者误判为接口实现者。</summary>
    [TestMethod]
    public void Port_implementation_scanner_ignores_constructor_consumers()
    {
        var implementations = ModuleBoundaryDebtScanner.ScanPortImplementations(
            ArchitectureRepositoryRoot.Find());

        Assert.AreEqual("files", implementations["IHostFileReferenceClaimService"]);
    }

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
