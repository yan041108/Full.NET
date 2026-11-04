using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>多会话模式下验证退出 A 的 HTTP/后台/连接撤权、B 的独立会话保持以及返回 Host。</summary>
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
        using var factory = new FullNetApiFactory(provider, connection, new Dictionary<string, string?>
        {
            // 两个会话为规定场景，不把同客户端登录的主动单会话策略混入成员撤权结论。
            ["Identity:SessionLoginPolicy"] = "AllowMultiple",
        });
        await TenantMembershipAssertions.VerifySelfLeaveAndReturnToHostAsync(factory);
    }
}
