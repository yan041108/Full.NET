using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;
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
    public async Task Identity_organization_unit_projection_reconciliation_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityOrganizationUnitProjectionOperationsAssertions
            .VerifyBoundedReconciliationAsync(factory);
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

    [TestMethod]
    public async Task Tenant_position_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await OrganizationPositionManagementAssertions
            .VerifyTenantPositionManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_user_position_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await OrganizationUserPositionManagementAssertions
            .VerifyTenantUserPositionManagementContractAsync(factory);
    }
}
