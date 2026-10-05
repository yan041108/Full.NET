using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 MySQL 验证原会话失败重试与条件更新。</summary>
[TestClass]
public sealed class AiKnowledgeIndexRetryApiMySqlTests
{
    [TestMethod]
    public async Task Http_retry_preserves_session_and_recovery_data_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiKnowledgeIndexRetryApiAssertions.VerifyAsync(factory);
    }
}
