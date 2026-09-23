using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.EnterpriseRequest;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class EnterpriseRequestApiSqlServerTests
{
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
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

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
    public async Task Tenant_demo_enterprise_requests_csv_import()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            ImportExportSyncSettings);

        await EnterpriseRequestAssertions.VerifyTenantDemoEnterpriseRequestsCsvImportAsync(factory);
    }
}