using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>验证 SQL Server 与 MySQL 同场景的知识库边界。</summary>
[TestClass]
public sealed class AiKnowledgeBaseApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_private_catalog_and_explicit_policy_are_enforced()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiKnowledgeBaseApiAssertions.VerifyAsync(factory);
    }
}
