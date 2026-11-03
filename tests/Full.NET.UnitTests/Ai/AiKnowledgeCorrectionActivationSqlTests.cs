using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>人工代次运行态与归档态的固定 SQL 边界；实际事务与竞争另由双库验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionActivationSqlTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer, false)] [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)] [DataRow(DatabaseProvider.MySql, true)]
    public void Running_checks_actual_source_revision_and_grant_while_archive_keeps_frozen_identity(DatabaseProvider provider, bool tenant)
    {
        var source = AiKnowledgeCorrectionActivationSql.AuthorityFor(provider, tenant);
        Assert.AreEqual(tenant ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, source.Scope);
        StringAssert.Contains(source.Text, "task.CorrectionRevisionId IS NOT NULL"); StringAssert.Contains(source.Text, "@OriginalPayloadHash");
        StringAssert.Contains(source.Text, "origin.InitialPayload"); StringAssert.Contains(source.Text, "revision.Payload");
        StringAssert.Contains(source.Text, "origin.ParserRuntimeId=@ParserRuntimeId"); StringAssert.Contains(source.Text, "grant_row.IsRevoked=1");
        StringAssert.Contains(source.Text, "AND (@Completed=1 OR (1=1"); StringAssert.Contains(source.Text, "task.CorrectionSourceId=@CorrectionSourceId");
        var complete = tenant ? AiKnowledgeCorrectionActivationSql.CompleteTenant : AiKnowledgeCorrectionActivationSql.CompleteHost;
        StringAssert.Contains(complete.Text, "AND CorrectionRevisionId IS NOT NULL AND StateKey");
        // 自动分支仍须全部字段为空，不能通过宽泛替换把空头分支变成绕过人工头的捷径。
        StringAssert.Contains(complete.Text, "fn_ai_knowledge_index_task.CorrectionRevisionId IS NULL");
        var manifest = AiKnowledgeCorrectionActivationSql.ManifestFor(provider, tenant, true);
        Assert.IsTrue(manifest.Text.StartsWith("SELECT manifest.Id FROM", StringComparison.Ordinal));
        StringAssert.Contains(manifest.Text, "@ManifestHash"); StringAssert.Contains(manifest.Text, "16777215");
        if (provider == DatabaseProvider.MySql) { Assert.IsTrue(source.Text.EndsWith(" FOR UPDATE", StringComparison.Ordinal)); StringAssert.Contains(source.Text, "SHA2(origin.OriginalPayload,256)"); }
        else StringAssert.Contains(source.Text, "HASHBYTES('SHA2_256',origin.OriginalPayload)");
    }

    [TestMethod] [DataRow(DatabaseProvider.SqlServer, false)] [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)] [DataRow(DatabaseProvider.MySql, true)]
    public void Complete_receipt_set_is_bounded_and_candidate_pointer_does_not_read_multiple_archives(DatabaseProvider provider, bool tenant)
    {
        var rows = AiKnowledgeCorrectionVectorSql.Receipts(provider, tenant);
        StringAssert.Contains(rows.Text, "@ExpectedBatchCount"); StringAssert.Contains(rows.Text, "ledger.SettledAtUtc IS NOT NULL");
        var guard = rows.Text.IndexOf(" AND fn_ai_knowledge_index_task.CorrectionRevisionId IS NOT NULL", StringComparison.Ordinal);
        Assert.IsTrue(guard > 0 && guard < rows.Text.IndexOf(" ORDER BY ", StringComparison.Ordinal));
        StringAssert.Contains(rows.Text, "2049");
        var pointer = tenant ? AiKnowledgeIndexActivationSql.ExistingPointerTenant : AiKnowledgeIndexActivationSql.ExistingPointerHost;
        StringAssert.Contains(pointer.Text, "FROM fn_ai_knowledge_active_generation AS active");
        Assert.IsFalse(pointer.Text.Contains("FROM fn_ai_knowledge_generation AS active", StringComparison.Ordinal));
        Assert.IsFalse(pointer.Text.Contains("binding.", StringComparison.Ordinal));
        Assert.AreEqual(tenant ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, pointer.Scope);
    }
}
