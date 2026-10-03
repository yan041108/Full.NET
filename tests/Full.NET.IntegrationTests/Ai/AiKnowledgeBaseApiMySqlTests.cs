using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>验证 MySQL 的所有者隔离、审批与并发 HTTP 行为。</summary>
[TestClass]
public sealed class AiKnowledgeBaseApiMySqlTests
{
    [TestMethod]
    public async Task MySql_private_catalog_and_explicit_policy_are_enforced()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiKnowledgeBaseApiAssertions.VerifyAsync(factory);
    }
}
