using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Ai;

/// <summary>逐块维护预览保留完整真实来源；显示 OCR 不表示已具备索引资格。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionWorkbenchTests
{
    [TestMethod]
    public void Merged_different_pages_keep_complete_original_context_and_supplement_has_no_coordinates()
    {
        var source = AiKnowledgeParsedDocument.FromPdf(new(new string('a', 64), 20, [new(1, "第一页原文"), new(2, "第二页原文")]), default)!;
        var initial = AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default); var first = initial.Blocks[0]; var second = initial.Blocks[1];
        var draft = initial.Replace(second.Id, second.Parts[0].Id, "很长的更正版本与原识别长度不同", default).Merge(first.Id, second.Id, default);
        draft = draft.Append(first.Id, "人工补写", Guid.CreateVersion7, default);
        draft = draft.Merge(first.Id, draft.Blocks[1].Id, default);
        var workspace = Workspace(draft); var response = AiKnowledgeCorrectionWorkbenchService.Project(workspace, initial, 0, false, default)!;
        CollectionAssert.AreEqual(new[] { "original", "corrected", "supplement" }, response.Block.Parts.Select(p => p.ContentKind).ToArray());
        Assert.AreEqual("第一页原文", response.Block.Parts[0].OriginalText); Assert.AreEqual(1, response.Block.Parts[0].SourceLocation!.PageNumber);
        var corrected = response.Block.Parts[1]; Assert.AreEqual("第二页原文", corrected.OriginalText);
        Assert.AreEqual(2, corrected.SourceLocation!.PageNumber); Assert.AreEqual("第二页原文".Length, corrected.SourceLength);
        Assert.AreNotEqual(corrected.Text.Length, corrected.SourceLength);
        var supplement = response.Block.Parts[2]; Assert.IsNull(supplement.AnchorId); Assert.IsNull(supplement.OriginalText);
        Assert.IsNull(supplement.SourceOffset); Assert.IsNull(supplement.SourceLength); Assert.IsNull(supplement.SourceLocation);
        var json = JsonSerializer.Serialize(response, AiJsonSerializerContext.Default.AiKnowledgeCorrectionWorkspaceResponse);
        var recovered = JsonSerializer.Deserialize(json, AiJsonSerializerContext.Default.AiKnowledgeCorrectionWorkspaceResponse)!;
        Assert.AreEqual(response.SourceId, recovered.SourceId); Assert.AreEqual(3, recovered.Block.Parts.Count);
        Assert.AreEqual("supplement", recovered.Block.Parts[2].ContentKind);
    }

    [TestMethod]
    public void Empty_cells_follow_initial_part_identity_after_merging_and_do_not_steal_other_cell_positions()
    {
        var source = AiKnowledgeParsedDocument.FromBlocks(AiKnowledgeParsedFormat.Csv, AiKnowledgeParsedStructure.MultiFormatParserVersion, new string('a', 64), 20,
            [new(0, "", "\t", new(AiKnowledgeSourcePositionKind.CsvCell, 0, 0, Grid: new(1, "CSV", 1, 1, "text"))),
             new(1, "非空", "\t", new(AiKnowledgeSourcePositionKind.CsvCell, 0, 2, Grid: new(1, "CSV", 1, 2, "text"))),
             new(2, "", "", new(AiKnowledgeSourcePositionKind.CsvCell, 0, 0, Grid: new(1, "CSV", 1, 3, "text")))], default)!;
        Assert.IsNotNull(source); var initial = AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default);
        var merged = initial.Merge(initial.Blocks[0].Id, initial.Blocks[1].Id, default).Merge(initial.Blocks[0].Id, initial.Blocks[2].Id, default);
        var response = AiKnowledgeCorrectionWorkbenchService.Project(Workspace(merged), initial, 0, false, default)!;
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, response.Block.Parts.Select(p => p.SourceLocation!.Grid!.Column).ToArray());
        Assert.AreEqual("", response.Block.Parts[2].OriginalText); Assert.AreEqual(0, response.Block.Parts[2].SourceLength);
        var edited = initial.Replace(initial.Blocks[2].Id, initial.Blocks[2].Parts[0].Id, "新增", default);
        var supplement = AiKnowledgeCorrectionWorkbenchService.Project(Workspace(edited), initial, 2, false, default)!.Block.Parts[0];
        Assert.AreEqual("supplement", supplement.ContentKind); Assert.IsNull(supplement.SourceLocation);
    }

    [TestMethod]
    public void Low_confidence_original_remains_original_and_keeps_measured_confidence_in_preview()
    {
        const string text = "待核对"; var hash = new string('a', 64);
        var source = AiKnowledgeParsedDocument.FromBlocks(AiKnowledgeParsedFormat.Image, AiKnowledgeParsedStructure.MultiFormatParserVersion, hash, 20,
            [new(0, text, "", new(AiKnowledgeSourcePositionKind.ImageRegion, 0, text.Length,
                Image: new(1, 100, 100, 1, 2, 90, 80, 0.4, "test-recognizer-sha256", 100, 100, 1, hash, "image-rgb-exif-white-v1")))], default)!;
        Assert.IsNotNull(source); var initial = AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default);
        var response = AiKnowledgeCorrectionWorkbenchService.Project(Workspace(initial), initial, 0, true, default)!;
        Assert.IsTrue(response.IsSourceView); Assert.AreEqual("original", response.Block.Parts[0].ContentKind);
        Assert.AreEqual(0.4, response.Block.Parts[0].SourceLocation!.Image!.Confidence);
        Assert.IsFalse(AiKnowledgeSourceLocations.Valid(response.Block.Parts[0].SourceLocation), "预览不能把低置信度当成已确认索引来源。");
    }

    [TestMethod]
    public void Page_bounds_cancellation_and_source_fields_are_independent_of_edited_text()
    {
        var text = new string('文', 2047) + "😀继续"; var bytes = Encoding.UTF8.GetBytes(text);
        var source = AiKnowledgeParsedDocument.FromText(new(text, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length), AiKnowledgeTextFormat.PlainText, default)!;
        var initial = AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default); var workspace = Workspace(initial);
        var first = AiKnowledgeCorrectionWorkbenchService.Project(workspace, initial, 0, true, default)!;
        var second = AiKnowledgeCorrectionWorkbenchService.Project(workspace, initial, 1, true, default)!;
        Assert.AreEqual(2047, first.Block.Parts[0].Text.Length); Assert.AreEqual("😀继续", second.Block.Parts[0].Text);
        Assert.AreEqual(2047, second.Block.Parts[0].SourceOffset); Assert.AreEqual(first.SourceHash, second.SourceHash);
        Assert.IsNull(AiKnowledgeCorrectionWorkbenchService.Project(workspace, initial, -1, true, default));
        Assert.IsNull(AiKnowledgeCorrectionWorkbenchService.Project(workspace, initial, 2, true, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeCorrectionWorkbenchService.Project(workspace, initial, 0, true, cancelled.Token));
    }

    [TestMethod]
    [DataRow("session")] [DataRow("tenant")] [DataRow("scope")] [DataRow("stamp")] [DataRow("runtime")] [DataRow("version")]
    public void Preparation_intent_binds_original_identity_and_runtime(string changed)
    {
        const string text = "原文"; var source = AiKnowledgeParsedDocument.FromText(new(text, new string('a', 64), 8), AiKnowledgeTextFormat.PlainText, default)!;
        var context = Workspace(AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default)).Context;
        var actor = new SessionBindingSnapshot(context.Binding.OwnerUserId, null, Guid.CreateVersion7(), "stamp", "host", "host");
        const string runtime = "queue:knowledge-correction-v1"; var before = AiKnowledgeCorrectionPreparationService.RequestHash(context, actor, runtime, default);
        if (changed == "session") actor = actor with { SessionId = Guid.CreateVersion7() };
        if (changed == "tenant") actor = actor with { TenantId = Guid.CreateVersion7() };
        if (changed == "scope") actor = actor with { EffectiveScope = "tenant" };
        if (changed == "stamp") actor = actor with { SecurityStamp = "new" };
        if (changed == "version") context = context with { Binding = context.Binding with { DocumentVersion = 3 } };
        var after = AiKnowledgeCorrectionPreparationService.RequestHash(context, actor, changed == "runtime" ? "changed" : runtime, default);
        Assert.AreNotEqual(before, after); Assert.AreEqual(64, before.Length); Assert.IsTrue(before.All(char.IsAsciiHexDigit));
    }

    [TestMethod]
    public void Prepare_body_rejects_client_injected_runtime_or_session_and_worker_flags_are_independent()
    {
        var request = new AiKnowledgeCorrectionPrepareRequest(Guid.CreateVersion7(), 2);
        var json = JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.AiKnowledgeCorrectionPrepareRequest);
        Assert.AreEqual(request, JsonSerializer.Deserialize(json, AiJsonSerializerContext.Default.AiKnowledgeCorrectionPrepareRequest));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json[..^1] + ",\"sessionId\":\"client\"}", AiJsonSerializerContext.Default.AiKnowledgeCorrectionPrepareRequest));
        var options = new AiKnowledgeIndexWorkerOptions { CorrectionPreparationEnabled = true };
        Assert.IsFalse(options.Enabled); Assert.IsTrue(options.CorrectionPreparationEnabled);
    }

    private static AiKnowledgeCorrectionWorkspace Workspace(AiKnowledgeCorrectionDocument document)
    {
        var binding = new AiKnowledgeCorrectionStore.Binding(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2);
        var version = new AiKnowledgeVersionRecord { Id = binding.VersionId, DocumentId = binding.DocumentId, OriginalFileName = "source.txt",
            ContentType = "text/plain", ContentHash = document.Source.ContentHash, SizeBytes = document.Source.SizeBytes };
        var snapshot = new AiKnowledgeCorrectionSnapshot(new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64), new string('b', 64)), "managed:test-v1", document);
        return new(new(binding, version), snapshot);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Preparation_cannot_enable_a_disabled_legacy_parser_merely_by_finding_its_image(bool enabled)
    {
        var image = "sha256:" + new string('a', 64);
        var service = new AiKnowledgeCorrectionPreparationService(null!, null!, null!, null!, null!,
            pdf: new("node", "bridge", image), pdfRuntime: Options.Create(new AiKnowledgePdfRuntimeOptions { Enabled = enabled }));
        Assert.AreEqual(enabled ? image : null, service.RuntimeFor("application/pdf"));
        Assert.AreEqual("managed:utf8-text-v1", service.RuntimeFor("text/plain"));
    }
}
