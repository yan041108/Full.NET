using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 SQL Server 验证原会话失败重试与条件更新。</summary>
[TestClass]
public sealed class AiKnowledgeIndexRetryApiSqlServerTests
{
    [TestMethod]
    public async Task Http_retry_preserves_session_and_recovery_data_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiKnowledgeIndexRetryApiAssertions.VerifyAsync(factory);
    }
}
