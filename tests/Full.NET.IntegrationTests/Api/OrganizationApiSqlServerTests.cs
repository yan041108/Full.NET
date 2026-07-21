using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Organization;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class OrganizationApiSqlServerTests
{
    [TestMethod]
    public async Task Tenant_unit_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await OrganizationUnitManagementAssertions.VerifyTenantUnitManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_unit_data_scope_filtering_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await OrganizationDataScopeFilteringAssertions.VerifyTenantUnitDataScopeFilteringAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_user_unit_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await OrganizationUserUnitManagementAssertions
            .VerifyTenantUserUnitManagementContractAsync(factory);
    }
}
