using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>验证 SQL Server 知识库成员读取及立即撤权。</summary>
[TestClass]
public sealed class AiKnowledgeMembersApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_members_are_read_only_scoped_and_immediately_revoked()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiKnowledgeMembersApiAssertions.VerifyAsync(factory);
    }
}
