using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeDocxCitationTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Docx_lineage_preserves_actual_lowercase_plan_hash_including_refusal(bool refused)
    {
        var original = AiKnowledgeAnswerLineageTests.Lineage();
        var lineage = original with { Version = 2, Dependencies = [original.Dependencies[0] with { PlanHash = new string('d', 64) }],
            References = refused ? [] : [original.References[0] with { PageNumber = null, DocxLocation = new(0, 1, null, null, null) }] };
        var json = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        var read = AiKnowledgeAnswerLineageCodec.Decode(json, lineage.MessageId, lineage.SessionId, lineage.ActorUserId, lineage.TenantId);
        Assert.AreEqual(lineage.Dependencies[0].PlanHash, read.Dependencies[0].PlanHash);
        Assert.AreEqual(refused ? 0 : 1, read.References.Count);
    }
    [TestMethod] [DataRow("null")] [DataRow("\"unexpected\"")] [DataRow("17")] [DataRow("[]")]
    public void Non_object_reference_is_hidden_instead_of_breaking_history(string value)
    {
        var lineage = AiKnowledgeAnswerLineageTests.Lineage();
        var json = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        using var parsed = JsonDocument.Parse(json);
        json = json.Replace(parsed.RootElement.GetProperty("references").GetRawText(), $"[{value}]", StringComparison.Ordinal);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(json, lineage.MessageId, lineage.SessionId, lineage.ActorUserId, lineage.TenantId));
    }
    [TestMethod]
    public void Malformed_version_with_docx_location_fails_closed()
    {
        var original = AiKnowledgeAnswerLineageTests.Lineage();
        var lineage = original with { Version = 2, References = [original.References[0] with { PageNumber = null, DocxLocation = new(1, 2, null, null, null) }] };
        var json = AiKnowledgeAnswerLineageCodec.Encode(lineage).Replace("\"version\":2", "\"version\":\"2\"", StringComparison.Ordinal);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(json, lineage.MessageId, lineage.SessionId, lineage.ActorUserId, lineage.TenantId));
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Version_two_lineage_roundtrips_docx_paragraph_and_table_coordinates(bool table)
    {
        var original = AiKnowledgeAnswerLineageTests.Lineage();
        var reference = original.References[0] with { PageNumber = null, DocxLocation = new(7, 8, table ? 2 : null, table ? 3 : null, table ? 4 : null) };
        var lineage = original with { Version = 2, References = [reference] };
        var json = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        var read = AiKnowledgeAnswerLineageCodec.Decode(json, lineage.MessageId, lineage.SessionId, lineage.ActorUserId, lineage.TenantId);
        Assert.AreEqual(reference, read.References[0]); Assert.AreEqual(2, read.Version);
    }

    [TestMethod]
    public void Server_context_includes_logical_location_without_inventing_page()
    {
        var chunk = AiKnowledgeRetrievalRuleTests.Chunk("application/vnd.openxmlformats-officedocument.wordprocessingml.document", 0, AiKnowledgeRetrievalRuleTests.Vector(1, 0)) with { PageNumber = null, DocxLocation = new(3, 4, 1, 2, 3) };
        var context = AiKnowledgeAnswerContextTests.Make(chunk);
        using var json = JsonDocument.Parse(context.Json); var source = json.RootElement.GetProperty("sources")[0];
        Assert.AreEqual(JsonValueKind.Null, source.GetProperty("page").ValueKind);
        Assert.AreEqual(2, source.GetProperty("docxLocation").GetProperty("rowNumber").GetInt32());
    }

    [TestMethod] [DataRow(1)] [DataRow(2)]
    public void Docx_geometry_rejects_page_or_partial_table(int version)
    {
        var original = AiKnowledgeAnswerLineageTests.Lineage();
        var reference = original.References[0] with { DocxLocation = new(7, 8, 2, null, 4) };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Encode(original with { Version = version, References = [reference] }));
    }
}
