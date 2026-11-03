using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Abstractions.Ids;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>人工引用不冒充原文；集合、上下文及持久协议都保留原文/更正/补写和全部真实位置。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionCitationTests
{
    [TestMethod]
    public void Merged_pages_project_all_origins_and_context_lengths_without_inventing_supplement_coordinates()
    {
        var plan = Plan(); var projection = AiKnowledgeCorrectionCitations.Create(plan, plan.Chunks.Single(), default);
        CollectionAssert.AreEqual(new[] { "original", "corrected", "supplement" }, projection.Spans.Select(s => s.ContentKind).ToArray());
        Assert.AreEqual(1, projection.Spans[0].SourceLocation!.PageNumber); Assert.AreEqual(2, projection.Spans[1].SourceLocation!.PageNumber);
        Assert.AreEqual("第二页原文".Length, projection.Spans[1].SourceLength); Assert.AreNotEqual(projection.Spans[1].TextLength, projection.Spans[1].SourceLength);
        Assert.IsNull(projection.Spans[2].SourceLocation); Assert.IsNull(projection.Spans[2].SourceOffset); Assert.IsNull(projection.Spans[2].SourceLength);
        Assert.IsTrue(AiKnowledgeCorrectionCitations.Valid(projection));
    }

    [TestMethod]
    public void Context_and_version_four_history_keep_null_top_level_coordinates_and_complete_evidence()
    {
        var plan = Plan(); var chunk = plan.Chunks.Single(); var correction = AiKnowledgeCorrectionCitations.Create(plan, chunk, default);
        var source = Source(plan); var retrieved = new AiKnowledgeRetrievalChunk(source, 0, null, 0, chunk.Text.Length, chunk.TokenCount, chunk.Text, new float[1536]) { Correction = correction };
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var context = AiKnowledgeAnswerContext.Create("来源是什么", [new(retrieved, 1)], ids, default);
        using var json = JsonDocument.Parse(context.Json); var item = json.RootElement.GetProperty("sources")[0];
        Assert.AreEqual(JsonValueKind.Null, item.GetProperty("offset").ValueKind); Assert.AreEqual(JsonValueKind.Null, item.GetProperty("length").ValueKind);
        Assert.AreEqual(3, item.GetProperty("correction").GetProperty("spans").GetArrayLength());
        var snapshot = new AiKnowledgeRetrievalSnapshot(Guid.CreateVersion7(), null, false, null!, [source.KnowledgeBaseId], [source], [new(retrieved, 1)]);
        var prepared = new AiKnowledgePreparedAnswer(snapshot, context); AiKnowledgeAnswerLineageCodec.EnsureFits(prepared);
        var lineage = AiKnowledgeAnswerLineageCodec.Create(new(prepared, new("答案", context.Citations, false), Guid.CreateVersion7(), Guid.CreateVersion7()), Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.AreEqual(4, lineage.Version); Assert.IsNull(lineage.References[0].Offset); Assert.IsNull(lineage.References[0].Length);
        var encoded = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        var decoded = AiKnowledgeAnswerLineageCodec.Decode(encoded, lineage.MessageId, lineage.SessionId, lineage.ActorUserId, null);
        Assert.AreEqual(correction.RevisionId, decoded.References[0].Correction!.RevisionId);
        CollectionAssert.AreEqual(correction.Spans.ToArray(), decoded.References[0].Correction!.Spans.ToArray());
        var bad = JsonNode.Parse(encoded)!; bad["references"]![0]!["correction"]!["spans"]![2]!["sourceOffset"] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(bad.ToJsonString(), lineage.MessageId, lineage.SessionId, lineage.ActorUserId, null));
        bad = JsonNode.Parse(encoded)!; bad["references"]![0]!["offset"] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(bad.ToJsonString(), lineage.MessageId, lineage.SessionId, lineage.ActorUserId, null));
    }

    [TestMethod] [DataRow("kind")] [DataRow("overlap")] [DataRow("range")] [DataRow("literal")]
    [DataRow("supplement")] [DataRow("source")] [DataRow("revision")]
    public void Malformed_human_reference_fails_closed(string fault)
    {
        var plan = Plan(); var value = AiKnowledgeCorrectionCitations.Create(plan, plan.Chunks.Single(), default); var spans = value.Spans.ToArray();
        switch (fault)
        {
            case "kind": spans[0] = spans[0] with { ContentKind = "model" }; break;
            case "overlap": spans[1] = spans[1] with { TextOffset = 0 }; break;
            case "range": spans[2] = spans[2] with { TextLength = int.MaxValue }; break;
            case "literal": spans[0] = spans[0] with { SourceLength = 1 }; break;
            case "supplement": spans[2] = spans[2] with { SourceLocation = spans[0].SourceLocation }; break;
            case "source": spans[0] = spans[0] with { SourceOffset = -1 }; break;
            case "revision": value = value with { RevisionId = Guid.Empty }; break;
        }
        Assert.IsFalse(AiKnowledgeCorrectionCitations.Valid(value with { Spans = spans }));
    }

    [TestMethod]
    public void Reviewed_ocr_keeps_its_original_low_confidence_and_unreviewed_coordinates_stay_rejected()
    {
        var location = new AiKnowledgeSourceLocationResponse(0, "image_region", null, null, null, null, null, null, null, null,
            new(1, 100, 100, 1, 2, 80, 70, 0.4, null, null, null, null));
        var span = new AiKnowledgeCorrectionSpanResponse("corrected", 0, 2, location, 0, 8);
        var value = new AiKnowledgeCorrectionCitationResponse(Guid.CreateVersion7(), 2, [span]);
        Assert.IsTrue(AiKnowledgeCorrectionCitations.Valid(value)); Assert.IsFalse(AiKnowledgeSourceLocations.Valid(location));
        Assert.IsFalse(AiKnowledgeCorrectionCitations.Valid(value with { Spans = [span with { ContentKind = "original", SourceLength = 2 }] }));
        Assert.AreEqual(0.4, value.Spans[0].SourceLocation!.Image!.Confidence);
    }

    [TestMethod]
    public void Capture_uses_one_bounded_count_and_never_enumerates_the_supplier()
    {
        var rows = new CountOnce([new("supplement", 0, 2, null, null, null)]);
        var captured = AiKnowledgeCorrectionCitations.Capture(new(Guid.CreateVersion7(), 2, rows));
        Assert.IsNotNull(captured); Assert.AreEqual(1, rows.Reads); rows.Items[0] = rows.Items[0] with { TextLength = 1 };
        Assert.AreEqual(2, captured.Spans[0].TextLength);
    }

    [TestMethod]
    public void Metadata_that_cannot_fit_history_is_rejected_before_generation()
    {
        var rows = Enumerable.Range(0, 8).Select(_ => AiKnowledgeAnswerLineageTests.Source()).Select(s => s with { ActiveTaskId = s.TaskId, DataClassification = "internal" }).ToArray();
        var revision = Guid.CreateVersion7(); rows[0] = rows[0] with { CorrectionRevisionId = revision };
        var location = new AiKnowledgeSourceLocationResponse(0, "html_node", null, null, null, null, null, null, null, "/" + new string('a', 1023), null);
        var spans = Enumerable.Range(0, 12).Select(i => new AiKnowledgeCorrectionSpanResponse("corrected", i, 1, location, 0, 1)).ToArray();
        var chunk = new AiKnowledgeRetrievalChunk(rows[0], 0, null, 0, 12, 12, "abcdefghijkl", new float[1536]) { Correction = new(revision, 12, spans) };
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var context = AiKnowledgeAnswerContext.Create("来源", [new(chunk, 1)], ids, default);
        var snapshot = new AiKnowledgeRetrievalSnapshot(Guid.CreateVersion7(), null, false, null!, rows.Select(r => r.KnowledgeBaseId).ToArray(), rows, [new(chunk, 1)]);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.EnsureFits(new(snapshot, context)));
    }

    private sealed class CountOnce(AiKnowledgeCorrectionSpanResponse[] items) : IReadOnlyList<AiKnowledgeCorrectionSpanResponse>
    {
        internal int Reads; internal AiKnowledgeCorrectionSpanResponse[] Items = items;
        public int Count => ++Reads == 1 ? Items.Length : throw new AssertFailedException("Count read twice.");
        public AiKnowledgeCorrectionSpanResponse this[int index] => Items[index];
        public IEnumerator<AiKnowledgeCorrectionSpanResponse> GetEnumerator() => throw new AssertFailedException("Supplier enumeration is forbidden.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static AiKnowledgeIndexStatusRecord Source(AiKnowledgeCorrectionIndexPlan plan)
    {
        var row = AiKnowledgeAnswerLineageTests.Source(); return row with { ActiveTaskId = row.TaskId, DataClassification = "internal", CorrectionRevisionId = plan.Snapshot.Identity.RevisionId };
    }
    private static AiKnowledgeCorrectionIndexPlan Plan()
    {
        var original = AiKnowledgeParsedDocument.FromPdf(new(new string('a', 64), 30, [new(1, "第一页原文"), new(2, "第二页原文")]), default)!;
        var draft = AiKnowledgeCorrectionDocument.Create(original, Guid.CreateVersion7, default); var first = draft.Blocks[0]; var second = draft.Blocks[1];
        draft = draft.Replace(second.Id, second.Parts[0].Id, "人工更正后的第二页不同长度正文", default).Merge(first.Id, second.Id, default);
        draft = draft.Append(first.Id, "明确补写", Guid.CreateVersion7, default); draft = draft.Merge(first.Id, draft.Blocks[1].Id, default);
        const string runtime = "managed:test-v1"; var bytes = AiKnowledgeCorrectionWireCodec.Encode(draft, runtime, default);
        var snapshot = new AiKnowledgeCorrectionSnapshot(new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            AiKnowledgeCorrectionWireCodec.SourceHash(original, runtime, default), Convert.ToHexStringLower(SHA256.HashData(bytes))), runtime, draft);
        var result = AiKnowledgeCorrectionIndexPlan.Create(snapshot, new(Guid.CreateVersion7(), 1, null, Guid.CreateVersion7(), 1,
            "openai_compatible", "text-embedding-3-small", "internal", new string('a', 64)), default); Assert.IsNotNull(result); return result;
    }
}
