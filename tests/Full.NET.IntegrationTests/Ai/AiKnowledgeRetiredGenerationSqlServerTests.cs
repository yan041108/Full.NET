using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 旧代次保留、指针变化、取消与失败恢复。</summary>
[TestClass]
public sealed class AiKnowledgeRetiredGenerationSqlServerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Worker_retires_only_superseded_payload_and_preserves_audit(bool pdf) =>
        await AiKnowledgeRetiredGenerationAssertions.VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), pdf);
}
