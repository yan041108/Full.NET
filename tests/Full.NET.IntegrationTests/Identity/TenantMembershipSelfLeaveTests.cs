using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>本人退出真实 HTTP 旅程：精确回执、原租户拒绝和全局账号返回 Host。</summary>
[TestClass]
public sealed class TenantMembershipSelfLeaveTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Leaving_current_tenant_retains_account_and_allows_return_to_host(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection);
        await TenantMembershipAssertions.VerifySelfLeaveAndReturnToHostAsync(factory);
    }
}
