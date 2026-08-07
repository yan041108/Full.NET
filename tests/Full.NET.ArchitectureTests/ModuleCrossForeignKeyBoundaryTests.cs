namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class ModuleCrossForeignKeyBoundaryTests
{
    [TestMethod]
    public void Foreign_key_gate_matches_catalog()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var discovered = ModuleBoundaryDebtScanner.ScanCrossModuleForeignKeys(root);
        var catalog = ModuleBoundaryDebtScanner.LoadForeignKeyCatalog(root);
        var violations = ModuleBoundaryDebtScanner.ValidateForeignKeyCatalog(root, discovered, catalog);

        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Foreign_key_gate_rejects_stale_entries()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var discovered = ModuleBoundaryDebtScanner.ScanCrossModuleForeignKeys(root);
        var sample = discovered.Length > 0
            ? discovered[0]
            : new ModuleBoundaryDebtScanner.CrossModuleForeignKey(
                "identity",
                "organization",
                "fn_identity_role_data_scope_unit",
                "UnitId",
                "fn_organization_unit",
                "Id",
                "FK_fn_identity_role_data_scope_unit_Unit",
                [
                    "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/083_IdentityRoleDataScopeUnitCrossModuleFk.sql",
                    "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/083_IdentityRoleDataScopeUnitCrossModuleFk.sql",
                ]);

        var valid = new[]
        {
            new ModuleBoundaryDebtScanner.CrossModuleForeignKeyDebt(
                sample.ConsumerModule,
                sample.OwnerModule,
                sample.ChildTable,
                sample.ChildColumn,
                sample.ReferencedTable,
                sample.ReferencedColumn,
                sample.ConstraintName,
                sample.MigrationFiles,
                "HostRoleDataScopeService",
                "Legacy cross-module foreign key.",
                "high",
                "module-data-consistency-boundary-20260807 Task 1 Step 4"),
        };

        Assert.HasCount(0, ModuleBoundaryDebtScanner.ValidateForeignKeyCatalog(root, [sample], valid));
        Assert.IsGreaterThan(
            0,
            ModuleBoundaryDebtScanner.ValidateForeignKeyCatalog(
                root,
                [sample],
                [valid[0] with { ChildTable = "fn_identity_role_data_scope_unit_stale" }]).Length);
    }
}