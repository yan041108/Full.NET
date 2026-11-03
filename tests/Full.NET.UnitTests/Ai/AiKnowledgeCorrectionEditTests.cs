using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Ai;

/// <summary>公开编辑使用封闭操作及服务器锚点；不允许把客户端声明伪装成原文。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionEditTests
{
    [TestMethod]
    public void Public_operations_preserve_original_partitions_and_supplement_without_client_assigned_anchors()
    {
        var document = Document("原文😀正文"); var first = document.Blocks[0]; var part = first.Parts[0];
        AiKnowledgeCorrectionDocument Apply(AiKnowledgeCorrectionEditOperation op) => AiKnowledgeCorrectionEditService.Apply(document, op, Guid.CreateVersion7, default);
        var replaced = Apply(new("replace", first.Id, part.Id, "人工更正不同长度", null, null));
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Corrected, replaced.Blocks[0].Parts[0].Origin);
        Assert.AreEqual("原文😀正文", replaced.Source.Blocks[0].Text);
        var split = Apply(new("split", first.Id, part.Id, null, 2, null)); Assert.AreEqual(2, split.Blocks.Count);
        Assert.AreNotEqual(part.AnchorId, split.Blocks[1].Parts[0].AnchorId);
        Assert.ThrowsExactly<InvalidDataException>(() => Apply(new("split", first.Id, part.Id, null, 3, null)));
        var appended = Apply(new("append", first.Id, null, "明确补写", null, null));
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Supplement, appended.Blocks[1].Parts[0].Origin); Assert.IsNull(appended.Blocks[1].Parts[0].AnchorId);
        var merged = AiKnowledgeCorrectionEditService.Apply(appended, new("merge", first.Id, null, null, null, null), Guid.CreateVersion7, default);
        Assert.AreEqual(2, merged.Blocks[0].Parts.Count);
        Assert.IsTrue(Apply(new("exclude", first.Id, null, null, null, true)).Blocks[0].Excluded);
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Corrected, Apply(new("confirm", first.Id, part.Id, null, null, null)).Blocks[0].Parts[0].Origin);
        Assert.ThrowsExactly<InvalidDataException>(() => Apply(new("merge", Guid.CreateVersion7(), null, null, null, null)));
    }
    [TestMethod]
    [DataRow("unknown")] [DataRow("missing-part")] [DataRow("extra-text")] [DataRow("extra-offset")] [DataRow("empty-id")]
    public void Ambiguous_or_incomplete_operation_shapes_are_rejected(string fault)
    {
        var op = new AiKnowledgeCorrectionEditOperation("replace", Guid.CreateVersion7(), Guid.CreateVersion7(), "更正", null, null);
        op = fault switch { "unknown" => op with { Kind = "delete" }, "missing-part" => op with { PartId = null },
            "extra-text" => op with { Kind = "confirm" }, "extra-offset" => op with { Offset = 1 }, _ => op with { BlockId = Guid.Empty } };
        Assert.IsFalse(AiKnowledgeCorrectionEditService.Valid(op));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionEditService.Apply(Document("原文"), op, Guid.CreateVersion7, default));
    }
    [TestMethod]
    public void Replay_fingerprint_uses_original_expected_document_version_but_binds_session_action_and_restore_target()
    {
        var binding = new AiKnowledgeCorrectionStore.Binding(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2);
        var context = new AiKnowledgeCorrectionContext(binding, new AiKnowledgeVersionRecord { Id = binding.VersionId, DocumentId = binding.DocumentId,
            FileId = Guid.CreateVersion7(), ContentHash = new string('a', 64), SizeBytes = 8, ContentType = "text/plain", OriginalFileName = "a.txt" });
        var actor = new SessionBindingSnapshot(binding.OwnerUserId, null, Guid.CreateVersion7(), "stamp", "host", "host");
        var expected = new AiKnowledgeCorrectionHeadRequest(2, Guid.CreateVersion7(), null, 0, new string('a', 64), new string('b', 64));
        var op = new AiKnowledgeCorrectionEditOperation("replace", Guid.CreateVersion7(), Guid.CreateVersion7(), "新文", null, null); var key = Guid.CreateVersion7();
        string Hash(AiKnowledgeCorrectionContext c, SessionBindingSnapshot a, AiKnowledgeCorrectionEditOperation? operation, Guid? source = null) =>
            AiKnowledgeCorrectionEditService.Fingerprint(c, a, key, expected, "人工核对", operation, source, null, default);
        var hash = Hash(context, actor, op);
        Assert.AreEqual(hash, Hash(context with { Binding = binding with { DocumentVersion = 3 } }, actor, op));
        Assert.AreNotEqual(hash, Hash(context, actor with { SessionId = Guid.CreateVersion7() }, op));
        Assert.AreNotEqual(hash, Hash(context, actor, op with { Text = "另一意图" }));
        Assert.AreNotEqual(hash, Hash(context, actor, null, expected.SourceId));
    }
    [TestMethod]
    public void Edit_json_rejects_injected_original_anchor_and_preserves_nullable_head_identity()
    {
        var request = new AiKnowledgeCorrectionEditRequest(Guid.CreateVersion7(), new(2, Guid.CreateVersion7(), null, 0, new string('a', 64), new string('b', 64)),
            "核对", new("replace", Guid.CreateVersion7(), Guid.CreateVersion7(), "更正", null, null));
        var json = JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.AiKnowledgeCorrectionEditRequest);
        Assert.AreEqual(request, JsonSerializer.Deserialize(json, AiJsonSerializerContext.Default.AiKnowledgeCorrectionEditRequest));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json[..^1] + ",\"anchors\":[]}", AiJsonSerializerContext.Default.AiKnowledgeCorrectionEditRequest));
    }
    private static AiKnowledgeCorrectionDocument Document(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var source = AiKnowledgeParsedDocument.FromText(new(text, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length), AiKnowledgeTextFormat.PlainText, default)!;
        return AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default);
    }
}
