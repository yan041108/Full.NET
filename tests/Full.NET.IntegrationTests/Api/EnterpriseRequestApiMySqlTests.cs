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
    public async Task Tenant_demo_enterprise_requests_xlsx_import()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            ImportExportSyncSettings, includeEnterpriseRequest: true);

        await EnterpriseRequestAssertions.VerifyTenantDemoEnterpriseRequestsXlsxImportAsync(factory);
    }
}
