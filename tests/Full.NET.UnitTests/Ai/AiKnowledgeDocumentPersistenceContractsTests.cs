using System.Text.Json.Nodes;
using Full.NET.Abstractions.Ids;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>统一格式的完整清单、向量和引用共同保留真实来源，不能以 TXT 位置替代。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentPersistenceContractsTests
{
    [TestMethod]
    [DataRow((int)AiKnowledgeParsedFormat.Pdf)] [DataRow((int)AiKnowledgeParsedFormat.Docx)]
    [DataRow((int)AiKnowledgeParsedFormat.Csv)] [DataRow((int)AiKnowledgeParsedFormat.Html)] [DataRow((int)AiKnowledgeParsedFormat.Excel)]
    [DataRow((int)AiKnowledgeParsedFormat.Word)] [DataRow((int)AiKnowledgeParsedFormat.Presentation)] [DataRow((int)AiKnowledgeParsedFormat.Image)]
    public void Full_manifest_vectors_context_and_lineage_preserve_native_coordinates(int formatValue)
    {
        var format = (AiKnowledgeParsedFormat)formatValue;
        var parsed = AiKnowledgeParsedWireTests.Fixture(format);
        var source = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(), parsed, new(), default).Output!
            with { ParserRuntimeId = AiKnowledgeIndexDocumentCodecTests.RuntimeId };
        var plan = AiKnowledgeDocumentPlan.Create(source, default); Assert.IsNotNull(plan);
        var bytes = AiKnowledgeIndexDocumentManifestCodec.Encode(plan, default);
        var restored = AiKnowledgeIndexDocumentManifestCodec.Decode(bytes, default);
        Assert.AreEqual(source.ParserRuntimeId, restored.Source.ParserRuntimeId); Assert.AreEqual(plan.DocumentHash, restored.DocumentHash);
        var snapshot = AiKnowledgeIndexDocumentVectorCodec.ReadBatch(bytes, 0,
            AiKnowledgeIndexDocumentVectorCodec.Encode(plan, 0, AiKnowledgeIndexDocumentCodecTests.Vectors(plan.Batches[0].Chunks.Count), default), default);
        var original = snapshot.Chunks[0]; Assert.AreEqual(source.Chunks[0], original.Source);
        var location = AiKnowledgeSourceLocations.Create(original.Block.Sequence, original.Source.Position, restored.Source.Document);
        Assert.IsTrue(AiKnowledgeSourceLocations.Valid(location));
        var row = new AiKnowledgeIndexStatusRecord { DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), DataClassification = "internal", ContentType = AiKnowledgeDocumentFormats.ContentType(parsed)! };
        var chunk = new AiKnowledgeRetrievalChunk(row, original.Source.Sequence, original.Source.Position.PageNumber,
            original.Source.Offset, original.Source.Length, original.Source.TokenCount, original.Source.Text, original.Vector) { SourceLocation = location };
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var context = AiKnowledgeAnswerContext.Create("来源位置？", [new(chunk, 1)], ids, default);
        Assert.IsTrue(JsonNode.Parse(context.Json)!["sources"]![0]!["sourceLocation"]!.ToJsonString().Contains(location.Kind, StringComparison.Ordinal));
        var hash = new string('A', 64); var message = Guid.CreateVersion7(); var session = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var dependency = new AiKnowledgeAnswerDependency(Guid.CreateVersion7(), row.DocumentId, row.VersionId, 1, 1, Guid.CreateVersion7(), hash, Guid.CreateVersion7(), hash, hash, plan.PlanHash);
        var reference = new AiKnowledgeAnswerReference(Guid.CreateVersion7(), row.DocumentId, row.VersionId, chunk.Sequence, chunk.PageNumber, chunk.Offset, chunk.Length) { SourceLocation = location };
        var lineage = new AiKnowledgeAnswerLineage(3, message, session, actor, null, [dependency], [reference]);
        var encoded = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        Assert.AreEqual(location, AiKnowledgeAnswerLineageCodec.Decode(encoded, message, session, actor, null).References[0].SourceLocation);
        var altered = JsonNode.Parse(encoded)!; altered["references"]![0]!["sourceLocation"]!["kind"] = "invented";
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(altered.ToJsonString(), message, session, actor, null));
    }

    [TestMethod]
    public void Runtime_asset_identity_is_required_and_changes_every_source_digest()
    {
        var source = AiKnowledgeIndexDocumentCodecTests.Plan(1).Source; var first = AiKnowledgeDocumentPlan.Create(source, default)!;
        Assert.IsNull(AiKnowledgeDocumentPlan.Create(source with { ParserRuntimeId = "" }, default));
        var changed = AiKnowledgeDocumentPlan.Create(source with { ParserRuntimeId = "sha256:" + new string('b', 64) }, default)!;
        Assert.AreNotEqual(first.DocumentHash, changed.DocumentHash); Assert.AreNotEqual(first.PlanHash, changed.PlanHash);
        Assert.AreNotEqual(first.Batches[0].SourceHash, changed.Batches[0].SourceHash);
    }
}
