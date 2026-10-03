using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 文档授权、删除与分页必须同 MySQL 语义一致。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentsApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_document_metadata_requires_independent_access_and_immediate_revocation()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiKnowledgeDocumentsApiAssertions.VerifyAsync(factory);
    }
}
