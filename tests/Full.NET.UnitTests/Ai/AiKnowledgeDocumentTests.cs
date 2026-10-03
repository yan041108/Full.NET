using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using System.Data;

namespace Full.NET.UnitTests.Ai;

/// <summary>文档草稿只保存有界元数据；空标题、超限文本和旧版本输入必须先拒绝。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentTests
{
    [TestMethod]
    public void Metadata_requires_a_nonempty_bounded_title()
    {
        Assert.IsNull(AiKnowledgeDocuments.Validate("  文档  ", null));
        Assert.IsNull(AiKnowledgeDocuments.Validate(new string('a', 200), new string('b', 2000)));
        Assert.IsNotNull(AiKnowledgeDocuments.Validate(null, null));
        Assert.IsNotNull(AiKnowledgeDocuments.Validate(" \t", null));
        Assert.IsNotNull(AiKnowledgeDocuments.Validate(new string('a', 201), null));
        Assert.IsNotNull(AiKnowledgeDocuments.Validate("文档", new string('b', 2001)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Native_projection_preserves_nullable_fields_and_utc_provider_timestamps(bool mysql)
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("KnowledgeBaseId", typeof(Guid));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("CreatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        table.Columns.Add("UpdatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        table.Columns.Add("Version", typeof(int));
        var id = Guid.CreateVersion7();
        var knowledge = Guid.CreateVersion7();
        var utc = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        table.Rows.Add(id, knowledge, "受保护标题", mysql ? "描述" : DBNull.Value,
            mysql ? (object)utc.UtcDateTime : utc, mysql ? utc.UtcDateTime : DBNull.Value, 7);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeDocument(reader);
        Assert.AreEqual(id, row.Id);
        Assert.AreEqual(knowledge, row.KnowledgeBaseId);
        Assert.AreEqual("受保护标题", row.Title);
        Assert.AreEqual(mysql ? "描述" : null, row.Description);
        Assert.AreEqual(utc, row.CreatedAtUtc);
        Assert.AreEqual(mysql ? utc : null, row.UpdatedAtUtc);
        Assert.AreEqual(7, row.Version);
    }
}
