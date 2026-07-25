using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Tenancy;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TenancyApiSqlServerTests
{
    [TestMethod]
    public async Task Anonymous_current_tenant_endpoint_returns_minimal_standard_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyApiAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_tenant_management_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyHostTenantManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_tenant_package_management_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyHostTenantPackageManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_tenant_package_assignment_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyHostTenantPackageAssignmentAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Provision_tenant_with_optional_package_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyProvisionTenantWithPackageAssertions.VerifyAsync(factory);
    }
}
