using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Organization;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class OrganizationApiMySqlTests
{
    [TestMethod]
    public async Task Tenant_unit_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await OrganizationUnitManagementAssertions.VerifyTenantUnitManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_unit_data_scope_filtering_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await OrganizationDataScopeFilteringAssertions.VerifyTenantUnitDataScopeFilteringAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_user_unit_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await OrganizationUserUnitManagementAssertions
            .VerifyTenantUserUnitManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_position_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await OrganizationPositionManagementAssertions
            .VerifyTenantPositionManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_user_position_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await OrganizationUserPositionManagementAssertions
            .VerifyTenantUserPositionManagementContractAsync(factory);
    }
}
