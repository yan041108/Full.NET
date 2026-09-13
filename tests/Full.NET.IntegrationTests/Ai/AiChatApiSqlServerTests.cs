using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 上的聊天 HTTP 预检与持久执行槽位契约。</summary>
[TestClass]
public sealed class AiChatApiSqlServerTests
{
    [TestMethod]
    public async Task Chat_preflight_returns_standard_http_errors_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiChatApiAssertions.VerifyAsync(factory);
    }
}
