using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>校对分区与真实来源不变量；不代表持久化、权限、索引或发布工作台验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionDocumentTests
{
    [TestMethod]
    public void Initial_preview_and_original_split_preserve_every_unicode_character()
    {
        var text = new string('a', 2047) + "😀末尾"; var document = Create(text);
        Assert.AreEqual(text, Render(document)); Assert.AreEqual(2047, document.Anchors[0].Length);
        var block = document.Blocks[1];
        Assert.ThrowsExactly<InvalidDataException>(() => document.Split(block.Id, block.Parts[0].Id, 1, Guid.CreateVersion7, default));
        var split = document.Split(block.Id, block.Parts[0].Id, 2, Guid.CreateVersion7, default);
        Assert.AreEqual(text, Render(split)); Assert.AreEqual(3, split.Anchors.Count);
        Assert.AreEqual(text, Render(document), "转换不能修改旧修订。");
    }
    [TestMethod]
    public void Corrected_text_split_retains_full_original_context_without_fabricating_offsets()
    {
        var original = Create("原始 OCR 文字"); var block = original.Blocks[0];
        var corrected = original.Replace(block.Id, block.Parts[0].Id, "校对后的更长正文", default);
        var split = corrected.Split(block.Id, block.Parts[0].Id, 3, Guid.CreateVersion7, default);
        Assert.AreEqual(1, split.Anchors.Count);
        foreach (var part in split.Blocks.SelectMany(b => b.Parts))
        {
            Assert.AreEqual(AiKnowledgeCorrectionOrigin.Corrected, part.Origin);
            Assert.AreEqual(0, split.Locate(part)!.Offset); Assert.AreEqual("原始 OCR 文字".Length, split.Locate(part)!.Length);
        }
        Assert.AreEqual("校对后的更长正文", Render(split));
    }
    [TestMethod]
    public void Merge_exclusion_and_supplement_keep_source_kind_and_original_content()
    {
        var original = Create("原文资料"); var first = original.Blocks[0];
        var appended = original.Append(first.Id, "人工补充", Guid.CreateVersion7, default);
        var supplement = appended.Blocks[1].Parts[0];
        Assert.IsNull(appended.Locate(supplement));
        var merged = appended.Merge(first.Id, appended.Blocks[1].Id, default);
        Assert.AreEqual(Render(appended), Render(merged));
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Supplement, merged.Blocks[0].Parts[1].Origin);
        var excluded = merged.Exclude(first.Id, true, default); Assert.IsTrue(excluded.Blocks[0].Excluded);
        Assert.AreEqual(Render(merged), Render(excluded)); Assert.IsFalse(merged.Blocks[0].Excluded);
        Assert.AreEqual("原文资料", original.Source.Blocks[0].Text);
    }
    [TestMethod]
    public void Saved_snapshot_rejects_missing_duplicated_and_reordered_original_partitions()
    {
        var original = Create("完整原始正文"); var first = original.Blocks[0];
        var split = original.Split(first.Id, first.Parts[0].Id, 2, Guid.CreateVersion7, default);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionDocument.Restore(split.Source, split.Anchors.Take(1).ToArray(), split.Blocks, default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionDocument.Restore(split.Source, [.. split.Anchors, split.Anchors[0] with { Id = Guid.CreateVersion7() }], split.Blocks, default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionDocument.Restore(split.Source, split.Anchors, split.Blocks.Reverse().ToArray(), default));
    }
    [TestMethod]
    public void Supplement_cannot_be_relabelled_as_original_or_given_an_existing_source_anchor()
    {
        var original = Create("真实原文"); var draft = original.Append(null, "补写", Guid.CreateVersion7, default);
        var blocks = draft.Blocks.ToArray(); var part = blocks[1].Parts[0];
        blocks[1] = blocks[1] with { Parts = [part with { Origin = AiKnowledgeCorrectionOrigin.Original }] };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionDocument.Restore(draft.Source, draft.Anchors, blocks, default));
        blocks[1] = blocks[1] with { Parts = [part with { AnchorId = draft.Anchors[0].Id }] };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionDocument.Restore(draft.Source, draft.Anchors, blocks, default));
    }
    [TestMethod]
    public void Editing_rejects_invalid_unicode_budget_and_mixed_exclusion_merge()
    {
        var original = Create("正文"); var first = original.Blocks[0];
        Assert.ThrowsExactly<InvalidDataException>(() => original.Replace(first.Id, first.Parts[0].Id, "\ud800", default));
        Assert.ThrowsExactly<InvalidDataException>(() => original.Append(null, new string('x', AiKnowledgeCorrectionDocument.MaxEditedCharacters + 1), Guid.CreateVersion7, default));
        var appended = original.Append(null, "补写", Guid.CreateVersion7, default).Exclude(first.Id, true, default);
        Assert.ThrowsExactly<InvalidDataException>(() => appended.Merge(first.Id, appended.Blocks[1].Id, default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => original.Exclude(first.Id, true, cancellation.Token));
    }
    private static AiKnowledgeCorrectionDocument Create(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var parsed = AiKnowledgeParsedDocument.FromText(new(text, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length), AiKnowledgeTextFormat.PlainText, default);
        Assert.IsNotNull(parsed); return AiKnowledgeCorrectionDocument.Create(parsed, Guid.CreateVersion7, default);
    }
    private static string Render(AiKnowledgeCorrectionDocument document) => string.Concat(document.Blocks.Select(block => string.Concat(block.Parts.Select(part => part.Prefix + part.Text)) + block.Separator));
}
