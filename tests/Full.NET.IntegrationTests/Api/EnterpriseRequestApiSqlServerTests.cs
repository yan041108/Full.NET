using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.EnterpriseRequest;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class EnterpriseRequestApiSqlServerTests
{
    [TestMethod]
    public async Task Enterprise_security_matrix_preserves_scope_ownership_and_concurrent_version()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" });
        await EnterpriseRequestAssertions.VerifySecurityMatrixAsync(factory);
    }

    [TestMethod]
    public async Task Ordinary_state_writes_and_cascade_delete_conflict_preserve_business_data()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            configureTestServices: EnterpriseRequestAssertions.ForceParentDeleteConflict);
        await EnterpriseRequestAssertions.VerifyOrdinaryStateWritesAndCascadeRollbackAsync(factory);
    }

    private static readonly IReadOnlyDictionary<string, string?> ImportExportSyncSettings =
        new Dictionary<string, string?>
        {
            ["FullNet:ImportExport:RunSynchronously"] = "true",
        };

    [TestMethod]
    public async Task Tenant_enterprise_request_crud_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            configureTestServices: EnterpriseRequestAssertions.ConfigureLineInsertFailure);

        await EnterpriseRequestAssertions.VerifyTenantCrudContractAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_submit_for_approval_when_definition_published()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await EnterpriseRequestAssertions.VerifyTenantSubmitForApprovalWhenDefinitionPublishedAsync(factory);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_submit_rejects_unprivileged_or_unassigned_actor(bool submitGranted)
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await EnterpriseRequestAssertions.VerifySubmitRejectsUnprivilegedOrUnassignedActorAsync(factory, submitGranted);
    }

    [TestMethod]
    public async Task Tenant_demo_enterprise_requests_workbook_import()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            ImportExportSyncSettings);

        await EnterpriseRequestAssertions.VerifyTenantDemoEnterpriseRequestsWorkbookImportAsync(factory);
    }
}
