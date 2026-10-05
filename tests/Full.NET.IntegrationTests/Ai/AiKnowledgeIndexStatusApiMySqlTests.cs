using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 当前授权和真实索引完整性读取。</summary>
[TestClass]
public sealed class AiKnowledgeIndexStatusApiMySqlTests
{
    [TestMethod]
    public async Task Http_status_preserves_read_authority_and_current_completeness_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiKnowledgeIndexStatusApiAssertions.VerifyAsync(factory);
    }
}
