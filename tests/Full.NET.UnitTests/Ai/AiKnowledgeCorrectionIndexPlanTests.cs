using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>人工输入的完整模型计划及来源证明；不冒充真实 OCR、费用或发布验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionIndexPlanTests
{
    [TestMethod] [DataRow(0)] [DataRow(4)] [DataRow(9)]
    public void Corrected_text_table_and_image_plans_restore_complete_evidence(int format)
    {
        var original = Source((AiKnowledgeParsedFormat)format); var draft = AiKnowledgeCorrectionDocument.Create(original, Guid.CreateVersion7, default);
        var block = draft.Blocks[0]; draft = draft.Replace(block.Id, block.Parts[0].Id, "经过人工核对的更长正文 😀", default);
        draft = draft.Append(block.Id, "人工补充资料", Guid.CreateVersion7, default);
        draft = draft.Merge(block.Id, draft.Blocks[1].Id, default); var plan = Plan(draft);
        Assert.AreEqual(2, plan.Chunks.Single().References.Count);
        Assert.AreEqual(original.Blocks[0].Position, plan.Chunks[0].References[0].Position);
        Assert.IsNull(plan.Chunks[0].References[1].Position); RoundTrip(plan);
    }

    [TestMethod]
    public void Merged_pages_keep_all_contexts_and_supplements_never_gain_a_position()
    {
        var original = AiKnowledgeParsedDocument.FromPdf(new("a".PadLeft(64, 'a'), 20,
            [new(1, "第一页正文"), new(2, "第二页正文")]), default)!;
        var draft = AiKnowledgeCorrectionDocument.Create(original, Guid.CreateVersion7, default); var first = draft.Blocks[0]; var second = draft.Blocks[1];
        draft = draft.Replace(second.Id, second.Parts[0].Id, "完全不同长度的人工改写", default).Merge(first.Id, second.Id, default);
        draft = draft.Append(first.Id, "人工补充", Guid.CreateVersion7, default); draft = draft.Merge(first.Id, draft.Blocks[1].Id, default);
        var plan = Plan(draft); var chunk = plan.Chunks.Single();
        Assert.AreEqual(3, chunk.References.Count);
        Assert.AreEqual(1, chunk.References[0].Position!.PageNumber); Assert.AreEqual(AiKnowledgeCorrectionOrigin.Original, chunk.References[0].Origin);
        Assert.AreEqual(2, chunk.References[1].Position!.PageNumber); Assert.AreEqual("第二页正文".Length, chunk.References[1].Position!.Length);
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Corrected, chunk.References[1].Origin);
        Assert.IsNull(chunk.References[2].Position); Assert.AreEqual(AiKnowledgeCorrectionOrigin.Supplement, chunk.References[2].Origin);
        RoundTrip(plan);
    }

    [TestMethod]
    public void Long_corrections_use_real_token_windows_and_full_context_for_every_intersection()
    {
        var draft = AiKnowledgeCorrectionDocument.Create(Source(AiKnowledgeParsedFormat.PlainText), Guid.CreateVersion7, default); var block = draft.Blocks[0];
        draft = draft.Replace(block.Id, block.Parts[0].Id, string.Concat(Enumerable.Repeat("校对😀内容 ", 1000)), default);
        var plan = Plan(draft); Assert.IsTrue(plan.Chunks.Count > 1); Assert.AreEqual(plan.TokenCount, plan.Chunks.Sum(c => (long)c.TokenCount));
        foreach (var chunk in plan.Chunks)
        {
            Assert.IsTrue(chunk.TokenCount <= 512); Assert.AreEqual(1, chunk.References.Count);
            Assert.AreEqual(0, chunk.References[0].Position!.Offset); Assert.AreEqual(draft.Source.Blocks[0].Text.Length, chunk.References[0].Position!.Length);
            Assert.IsFalse(char.IsLowSurrogate(chunk.Text[0])); Assert.IsFalse(char.IsHighSurrogate(chunk.Text[^1]));
        }
        RoundTrip(plan);
    }

    [TestMethod]
    public void Low_confidence_original_requires_explicit_confirmation_or_exclusion()
    {
        var source = Source(AiKnowledgeParsedFormat.Image); var original = source.Blocks[0];
        source = AiKnowledgeParsedDocument.FromBlocks(source.Format, source.ParserVersion, source.ContentHash, source.SizeBytes,
            [original with { Position = original.Position with { Image = original.Position.Image! with { Confidence = 0.4 } } }], default)!;
        var draft = AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default); var block = draft.Blocks[0];
        Assert.IsNull(AiKnowledgeCorrectionIndexPlan.Create(Snapshot(draft), Model(), default));
        var checkedDraft = draft.Confirm(block.Id, block.Parts[0].Id, default); Assert.IsNotNull(Plan(checkedDraft));
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Corrected, checkedDraft.Blocks[0].Parts[0].Origin);
        Assert.IsNull(AiKnowledgeCorrectionIndexPlan.Create(Snapshot(draft.Exclude(block.Id, true, default)), Model(), default));
    }

    [TestMethod]
    public void Identity_payload_destination_and_exclusion_are_part_of_the_entire_plan()
    {
        var draft = AiKnowledgeCorrectionDocument.Create(Source(AiKnowledgeParsedFormat.PlainText), Guid.CreateVersion7, default);
        var snapshot = Snapshot(draft); var model = Model(); var first = AiKnowledgeCorrectionIndexPlan.Create(snapshot, model, default)!;
        Assert.IsNull(AiKnowledgeCorrectionIndexPlan.Create(snapshot with { Identity = snapshot.Identity with { PayloadHash = new string('b', 64) } }, model, default));
        Assert.IsNull(AiKnowledgeCorrectionIndexPlan.Create(snapshot with { Identity = snapshot.Identity with { RevisionId = null } }, model, default));
        Assert.IsNull(AiKnowledgeCorrectionIndexPlan.Create(snapshot, model with { TenantId = Guid.Empty }, default));
        var next = AiKnowledgeCorrectionIndexPlan.Create(snapshot, model with { DestinationHash = new string('c', 64) }, default)!;
        Assert.AreNotEqual(first.PlanHash, next.PlanHash); Assert.AreNotEqual(first.Batches[0].SourceHash, next.Batches[0].SourceHash);
        var newRevision = AiKnowledgeCorrectionIndexPlan.Create(snapshot with { Identity = snapshot.Identity with { RevisionId = Guid.CreateVersion7() } }, model, default)!;
        Assert.AreNotEqual(first.PlanHash, newRevision.PlanHash);
        var excluded = draft.Append(null, "应排除的噪声", Guid.CreateVersion7, default); excluded = excluded.Exclude(excluded.Blocks[1].Id, true, default);
        Assert.IsFalse(Plan(excluded).Chunks.Any(c => c.Text.Contains("噪声", StringComparison.Ordinal)));
        Assert.AreNotEqual(first.PlanHash, Plan(excluded).PlanHash);
    }

    [TestMethod]
    public void Empty_cell_edit_is_supplement_and_cannot_be_confirmed_as_original()
    {
        var original = Source(AiKnowledgeParsedFormat.Csv); var cell = original.Blocks[0];
        var source = AiKnowledgeParsedDocument.FromBlocks(original.Format, original.ParserVersion, original.ContentHash, original.SizeBytes,
            [cell with { Text = "", Position = cell.Position with { Length = 0 } },
                cell with { Sequence = 1, Position = cell.Position with { Grid = cell.Position.Grid! with { Column = 2 } } }], default)!;
        var draft = AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default); var block = draft.Blocks[0];
        draft = draft.Replace(block.Id, block.Parts[0].Id, "空格内人工补写", default);
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Supplement, draft.Blocks[0].Parts[0].Origin);
        Assert.IsNull(Plan(draft).Chunks[0].References.Single().Position);
        Assert.ThrowsExactly<InvalidDataException>(() => draft.Confirm(block.Id, block.Parts[0].Id, default)); RoundTrip(Plan(draft));
    }

    [TestMethod]
    public void Manifest_rejects_truncation_wrong_authoritative_source_trailing_bytes_and_changed_summary()
    {
        var plan = Plan(AiKnowledgeCorrectionDocument.Create(Source(AiKnowledgeParsedFormat.PlainText), Guid.CreateVersion7, default));
        var bytes = AiKnowledgeCorrectionManifestCodec.Encode(plan, default); var raw = plan.Snapshot.Document.Source;
        foreach (var input in new[] { bytes[..7], bytes[..^1], bytes.Concat(new byte[1]).ToArray(), Changed(bytes) })
            Assert.ThrowsExactly<InvalidDataException>(() => Decode(input, raw));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionManifestCodec.Decode(bytes, raw.Format, new string('b', 64), raw.SizeBytes, raw.Policy, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeCorrectionManifestCodec.Decode(bytes, raw.Format, raw.ContentHash, raw.SizeBytes, raw.Policy, cancelled.Token));
        static byte[] Changed(byte[] source) { var copy = source.ToArray(); copy[^1] ^= 1; return copy; }
    }

    private static void RoundTrip(AiKnowledgeCorrectionIndexPlan plan)
    {
        var restored = Decode(AiKnowledgeCorrectionManifestCodec.Encode(plan, default), plan.Snapshot.Document.Source);
        Assert.AreEqual(plan.PlanHash, restored.PlanHash); Assert.AreEqual(plan.TokenCount, restored.TokenCount);
        Assert.AreEqual(plan.Snapshot.Identity, restored.Snapshot.Identity);
        for (var i = 0; i < plan.Chunks.Count; i++)
        { Assert.AreEqual(plan.Chunks[i].Text, restored.Chunks[i].Text); CollectionAssert.AreEqual(plan.Chunks[i].References.ToArray(), restored.Chunks[i].References.ToArray()); }
    }
    private static AiKnowledgeCorrectionIndexPlan Decode(byte[] bytes, AiKnowledgeParsedDocument raw) =>
        AiKnowledgeCorrectionManifestCodec.Decode(bytes, raw.Format, raw.ContentHash, raw.SizeBytes, raw.Policy, default);
    private static AiKnowledgeCorrectionIndexPlan Plan(AiKnowledgeCorrectionDocument draft)
    { var plan = AiKnowledgeCorrectionIndexPlan.Create(Snapshot(draft), Model(), default); Assert.IsNotNull(plan); return plan; }
    private static AiKnowledgeCorrectionModelBinding Model() => new(Guid.Parse("01900000-0000-7000-8000-000000000001"), 1, null,
        Guid.Parse("01900000-0000-7000-8000-000000000002"), 1, "openai_compatible", "text-embedding-3-small", "internal", new string('a', 64));
    private static AiKnowledgeCorrectionSnapshot Snapshot(AiKnowledgeCorrectionDocument draft)
    {
        const string runtime = "managed:test-v1"; var bytes = AiKnowledgeCorrectionWireCodec.Encode(draft, runtime, default);
        return new(new(Guid.Parse("01900000-0000-7000-8000-000000000003"), Guid.Parse("01900000-0000-7000-8000-000000000004"), 1,
            AiKnowledgeCorrectionWireCodec.SourceHash(draft.Source, runtime, default), Convert.ToHexStringLower(SHA256.HashData(bytes))), runtime, draft);
    }
    private static AiKnowledgeParsedDocument Source(AiKnowledgeParsedFormat format)
    {
        const string text = "原始 OCR 正文😀"; var hash = new string('a', 64); var size = Encoding.UTF8.GetByteCount(text);
        if (format == AiKnowledgeParsedFormat.PlainText) return AiKnowledgeParsedDocument.FromText(new(text, hash, size), AiKnowledgeTextFormat.PlainText, default)!;
        var position = format == AiKnowledgeParsedFormat.Csv
            ? new AiKnowledgeSourcePosition(AiKnowledgeSourcePositionKind.CsvCell, 0, text.Length, Grid: new(1, "CSV", 1, 1, "text"))
            : new AiKnowledgeSourcePosition(AiKnowledgeSourcePositionKind.ImageRegion, 0, text.Length,
                Image: new(1, 100, 100, 1, 2, 90, 80, 0.95, "test-recognizer-sha256", 100, 100, 1, hash, "image-rgb-exif-white-v1"));
        var parsed = AiKnowledgeParsedDocument.FromBlocks(format, AiKnowledgeParsedStructure.MultiFormatParserVersion, hash, size, [new(0, text, "", position)], default);
        Assert.IsNotNull(parsed); return parsed;
    }
}
