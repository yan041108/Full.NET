using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 HTTP 认证与 SQL Server 原会话原子入队。</summary>
[TestClass]
public sealed class AiKnowledgeIndexEnqueueApiSqlServerTests
{
    [TestMethod]
    public async Task Http_enqueue_binds_original_session_and_current_authority_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiKnowledgeIndexEnqueueApiAssertions.VerifyAsync(factory);
    }
}
