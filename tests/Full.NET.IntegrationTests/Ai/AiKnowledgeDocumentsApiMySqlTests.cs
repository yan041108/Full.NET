using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 文档授权、删除与分页必须同 SQL Server 语义一致。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentsApiMySqlTests
{
    [TestMethod]
    public async Task MySql_document_metadata_requires_independent_access_and_immediate_revocation()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiKnowledgeDocumentsApiAssertions.VerifyAsync(factory);
    }
}
