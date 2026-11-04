using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.EnterpriseRequest;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：EnterpriseRequestApiMySql。</summary>
[TestClass]
public sealed class EnterpriseRequestApiMySqlTests
{
    [TestMethod]
    public async Task Detail_write_parent_lock_serializes_workflow_submit()
    {
        await EnterpriseRequestMutationConcurrencyAssertions.VerifyAsync(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Draft_mutations_are_frozen_after_submit()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), includeEnterpriseRequest: true);
        await EnterpriseRequestDraftMutationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Bound_runtime_failure_preserves_submission_and_cancels_idempotently()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), includeEnterpriseRequest: true);
        await EnterpriseRequestAssertions.VerifyBoundRuntimeFailureAsync(factory);
    }

    [TestMethod]
    public async Task Enterprise_security_matrix_preserves_scope_ownership_and_concurrent_version()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            settingsOverrides: new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" }, includeEnterpriseRequest: true);
        await EnterpriseRequestAssertions.VerifySecurityMatrixAsync(factory);
    }

    [TestMethod]
    public async Task Ordinary_state_writes_and_cascade_delete_conflict_preserve_business_data()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: EnterpriseRequestAssertions.ForceParentDeleteConflict, includeEnterpriseRequest: true);
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
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), includeEnterpriseRequest: true);

        await EnterpriseRequestAssertions.VerifyTenantCrudContractAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_submit_for_approval_when_definition_published()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), includeEnterpriseRequest: true);

        await EnterpriseRequestAssertions.VerifyTenantSubmitForApprovalWhenDefinitionPublishedAsync(factory);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_submit_rejects_unprivileged_or_unassigned_actor(bool submitGranted)
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), includeEnterpriseRequest: true);
        await EnterpriseRequestAssertions.VerifySubmitRejectsUnprivilegedOrUnassignedActorAsync(factory, submitGranted);
    }

    [TestMethod]
    public async Task Tenant_demo_enterprise_requests_workbook_import()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            ImportExportSyncSettings, includeEnterpriseRequest: true);

        await EnterpriseRequestAssertions.VerifyTenantDemoEnterpriseRequestsWorkbookImportAsync(factory);
    }
}
