using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>验证 MySQL 知识库成员读取及立即撤权。</summary>
[TestClass]
public sealed class AiKnowledgeMembersApiMySqlTests
{
    [TestMethod]
    public async Task MySql_members_are_read_only_scoped_and_immediately_revoked()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiKnowledgeMembersApiAssertions.VerifyAsync(factory);
    }
}
