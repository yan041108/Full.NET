namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>补写没有原文坐标；校对后的区间只定位原始上下文，不冒充逐字引文。</summary>
internal enum AiKnowledgeCorrectionOrigin { Original, Corrected, Supplement }
/// <summary>原始正文的完整分区；来源身份不接受编辑器自由构造。</summary>
internal sealed record AiKnowledgeCorrectionAnchor(Guid Id, int BlockSequence, int Offset, int Length);
/// <summary>排版分隔符不参与原文引用；校对片段拆分后可以共享一个真实上下文锚点。</summary>
internal sealed record AiKnowledgeCorrectionPart(Guid Id, string Text, AiKnowledgeCorrectionOrigin Origin, Guid? AnchorId, string Prefix);
/// <summary>排除只影响后续索引选择，不能删除原解析与修订证据。</summary>
internal sealed record AiKnowledgeCorrectionBlock(Guid Id, bool Excluded, IReadOnlyList<AiKnowledgeCorrectionPart> Parts, string Separator);

/// <summary>校对工作台的不可变转换内核；不访问数据库、文件、模型或授权缓存。</summary>
internal sealed class AiKnowledgeCorrectionDocument
{
    internal const int MaxItems = 8192;
    internal const int InitialPartCharacters = 2048;
    internal const int MaxEditedCharacters = 32768;
    internal AiKnowledgeParsedDocument Source { get; }
    internal IReadOnlyList<AiKnowledgeCorrectionAnchor> Anchors { get; }
    internal IReadOnlyList<AiKnowledgeCorrectionBlock> Blocks { get; }

    private AiKnowledgeCorrectionDocument(AiKnowledgeParsedDocument source, AiKnowledgeCorrectionAnchor[] anchors, AiKnowledgeCorrectionBlock[] blocks)
    { Source = source; Anchors = Array.AsReadOnly(anchors); Blocks = Array.AsReadOnly(blocks); }

    /// <summary>首次预览按完整 Unicode 区间划分，保留所有非空原文和原解析分隔符。</summary>
    internal static AiKnowledgeCorrectionDocument Create(AiKnowledgeParsedDocument source, Func<Guid> ids, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(ids);
        var anchors = new List<AiKnowledgeCorrectionAnchor>(); var blocks = new List<AiKnowledgeCorrectionBlock>();
        foreach (var original in source.Blocks)
        {
            token.ThrowIfCancellationRequested();
            if (original.Text.Length == 0)
            {
                blocks.Add(new(ids(), false, [new(ids(), string.Empty, AiKnowledgeCorrectionOrigin.Original, null, string.Empty)], original.Separator));
                continue;
            }
            var offset = 0;
            while (offset < original.Text.Length)
            {
                var length = Math.Min(InitialPartCharacters, original.Text.Length - offset);
                if (!Boundary(original.Text, offset + length)) length--;
                var anchor = new AiKnowledgeCorrectionAnchor(ids(), original.Sequence, offset, length); anchors.Add(anchor);
                blocks.Add(new(ids(), false, [new(ids(), original.Text.Substring(offset, length), AiKnowledgeCorrectionOrigin.Original, anchor.Id, string.Empty)],
                    offset + length == original.Text.Length ? original.Separator : string.Empty));
                offset += length;
                if (blocks.Count > MaxItems) throw Invalid();
            }
        }
        return Restore(source, anchors, blocks, token);
    }

    /// <summary>持久快照恢复也验证完整分区与来源性质，不能靠已有摘要跳过结构检查。</summary>
    internal static AiKnowledgeCorrectionDocument Restore(AiKnowledgeParsedDocument source, IReadOnlyList<AiKnowledgeCorrectionAnchor> anchors,
        IReadOnlyList<AiKnowledgeCorrectionBlock> blocks, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(source);
        if (anchors is null || blocks is null || anchors.Count > MaxItems || blocks.Count > MaxItems) throw Invalid();
        var ownedAnchors = anchors.ToArray(); var ownedBlocks = new AiKnowledgeCorrectionBlock[blocks.Count];
        var identities = new HashSet<Guid>(); var byId = new Dictionary<Guid, AiKnowledgeCorrectionAnchor>(); var used = new Dictionary<Guid, List<AiKnowledgeCorrectionPart>>();
        foreach (var anchor in ownedAnchors)
        {
            token.ThrowIfCancellationRequested();
            if (anchor is null || anchor.Id == Guid.Empty || !identities.Add(anchor.Id) || anchor.BlockSequence < 0 || anchor.BlockSequence >= source.Blocks.Count
                || source.Blocks[anchor.BlockSequence].Locate(anchor.Offset, anchor.Length) is null) throw Invalid();
            byId.Add(anchor.Id, anchor);
        }
        // 原文的每个字符恰好由一个锚点覆盖；排除和拆分校对片段都不改变这个分区。
        var partitions = ownedAnchors.GroupBy(anchor => anchor.BlockSequence).ToDictionary(group => group.Key, group => group.OrderBy(anchor => anchor.Offset).ToArray());
        foreach (var original in source.Blocks)
        {
            var next = 0;
            foreach (var anchor in partitions.GetValueOrDefault(original.Sequence) ?? [])
            { if (anchor.Offset != next) throw Invalid(); next = checked(next + anchor.Length); }
            if (next != original.Text.Length) throw Invalid();
        }
        var order = ownedAnchors.OrderBy(anchor => anchor.BlockSequence).ThenBy(anchor => anchor.Offset)
            .Select((anchor, index) => (anchor.Id, index)).ToDictionary(item => item.Id, item => item.index);
        long characters = 0; var parts = 0; var previousAnchor = -1;
        for (var index = 0; index < blocks.Count; index++)
        {
            token.ThrowIfCancellationRequested(); var block = blocks[index];
            if (block is null || block.Id == Guid.Empty || !identities.Add(block.Id) || block.Parts is not { Count: > 0 and <= 32 } || !Layout(block.Separator)) throw Invalid();
            var ownedParts = block.Parts.ToArray();
            foreach (var part in ownedParts)
            {
                if (part is null || part.Id == Guid.Empty || !identities.Add(part.Id) || !Layout(part.Prefix)
                    || part.Text is null || !AiKnowledgeParsedDocument.ValidText(part.Text, token) || ++parts > MaxItems) throw Invalid();
                characters += part.Text.Length + part.Prefix.Length;
                if (characters > AiKnowledgeTextParser.MaxOutputCharacters) throw Invalid();
                if (part.Origin == AiKnowledgeCorrectionOrigin.Supplement)
                { if (part.AnchorId is not null) throw Invalid(); continue; }
                if (part.Origin == AiKnowledgeCorrectionOrigin.Original && part.AnchorId is null && part.Text.Length == 0) continue;
                if (part.Origin is not (AiKnowledgeCorrectionOrigin.Original or AiKnowledgeCorrectionOrigin.Corrected)
                    || part.AnchorId is not { } id || !byId.TryGetValue(id, out var anchor)) throw Invalid();
                if (order[id] < previousAnchor) throw Invalid();
                previousAnchor = order[id];
                if (part.Origin == AiKnowledgeCorrectionOrigin.Original
                    && part.Text != source.Blocks[anchor.BlockSequence].Text.Substring(anchor.Offset, anchor.Length)) throw Invalid();
                if (!used.TryGetValue(id, out var references)) used.Add(id, references = []);
                references.Add(part);
            }
            characters += block.Separator.Length;
            if (characters > AiKnowledgeTextParser.MaxOutputCharacters) throw Invalid();
            ownedBlocks[index] = block with { Parts = Array.AsReadOnly(ownedParts) };
        }
        if (used.Count != byId.Count || used.Values.Any(group => group.Count > 1 && group.Any(part => part.Origin != AiKnowledgeCorrectionOrigin.Corrected))) throw Invalid();
        return new(source, ownedAnchors, ownedBlocks);
    }

    /// <summary>更正不覆盖原识别值；恢复为原文字面值时，仅独占锚点的片段可重新标为原文。</summary>
    internal AiKnowledgeCorrectionDocument Replace(Guid blockId, Guid partId, string text, CancellationToken token)
    {
        ValidateEdit(text, token); var (bi, pi) = Find(blockId, partId); var part = Blocks[bi].Parts[pi];
        var origin = part.Origin;
        if (part.AnchorId is { } anchorId)
        {
            var anchor = Anchors.Single(a => a.Id == anchorId);
            origin = Blocks.SelectMany(b => b.Parts).Count(p => p.AnchorId == anchorId) == 1
                && Source.Blocks[anchor.BlockSequence].Text.Substring(anchor.Offset, anchor.Length) == text
                ? AiKnowledgeCorrectionOrigin.Original : AiKnowledgeCorrectionOrigin.Corrected;
        }
        // 空单元格没有字符锚点；填入文字属于人工补充，不能伪造零长原文引用。
        else if (text.Length > 0) origin = AiKnowledgeCorrectionOrigin.Supplement;
        var parts = Blocks[bi].Parts.ToArray(); parts[pi] = part with { Text = text, Origin = origin };
        var blocks = Blocks.ToArray(); blocks[bi] = blocks[bi] with { Parts = parts };
        return Restore(Source, Anchors, blocks, token);
    }

    /// <summary>低置信度识别即使字面未变，也须留下明确的人工核对性质；补写不能据此获得原文锚点。</summary>
    internal AiKnowledgeCorrectionDocument Confirm(Guid blockId, Guid partId, CancellationToken token)
    {
        var (bi, pi) = Find(blockId, partId); var part = Blocks[bi].Parts[pi];
        if (part.AnchorId is null) throw Invalid();
        var parts = Blocks[bi].Parts.ToArray(); parts[pi] = part with { Origin = AiKnowledgeCorrectionOrigin.Corrected };
        var blocks = Blocks.ToArray(); blocks[bi] = blocks[bi] with { Parts = parts };
        return Restore(Source, Anchors, blocks, token);
    }

    /// <summary>原文拆分真实分区；校对拆分共用完整原文上下文，绝不按编辑字符比例编造偏移。</summary>
    internal AiKnowledgeCorrectionDocument Split(Guid blockId, Guid partId, int offset, Func<Guid> ids, CancellationToken token)
    {
        var (bi, pi) = Find(blockId, partId); var block = Blocks[bi]; var part = block.Parts[pi];
        if (offset <= 0 || offset >= part.Text.Length || !Boundary(part.Text, offset)) throw Invalid();
        var anchors = Anchors.ToList(); var left = part with { Text = part.Text[..offset] };
        var right = part with { Id = ids(), Text = part.Text[offset..], Prefix = string.Empty };
        if (part.Origin == AiKnowledgeCorrectionOrigin.Original)
        {
            var anchor = Anchors.Single(a => a.Id == part.AnchorId); anchors.Remove(anchor);
            var a = anchor with { Id = ids(), Length = offset }; var b = anchor with { Id = ids(), Offset = anchor.Offset + offset, Length = anchor.Length - offset };
            anchors.Add(a); anchors.Add(b); left = left with { AnchorId = a.Id }; right = right with { AnchorId = b.Id };
        }
        var blocks = Blocks.ToList(); blocks[bi] = block with { Parts = [.. block.Parts.Take(pi), left], Separator = string.Empty };
        blocks.Insert(bi + 1, new(ids(), block.Excluded, [right, .. block.Parts.Skip(pi + 1)], block.Separator));
        return Restore(Source, anchors, blocks, token);
    }

    /// <summary>只合并相邻且排除状态相同的块，保持原始顺序及全部位置。</summary>
    internal AiKnowledgeCorrectionDocument Merge(Guid blockId, Guid nextBlockId, CancellationToken token)
    {
        var bi = Index(blockId); if (bi + 1 >= Blocks.Count || Blocks[bi + 1].Id != nextBlockId) throw Invalid();
        var first = Blocks[bi]; var next = Blocks[bi + 1];
        if (first.Excluded != next.Excluded) throw Invalid();
        var parts = next.Parts.ToArray(); parts[0] = parts[0] with { Prefix = first.Separator + parts[0].Prefix };
        var blocks = Blocks.ToList(); blocks[bi] = first with { Parts = [.. first.Parts, .. parts], Separator = next.Separator }; blocks.RemoveAt(bi + 1);
        return Restore(Source, Anchors, blocks, token);
    }

    /// <summary>人工补写没有原文锚点，不能在复制或重新组合后退化为原文。</summary>
    internal AiKnowledgeCorrectionDocument Append(Guid? afterBlockId, string text, Func<Guid> ids, CancellationToken token)
    {
        ValidateEdit(text, token); if (string.IsNullOrWhiteSpace(text)) throw Invalid();
        var bi = afterBlockId.HasValue ? Index(afterBlockId.Value) : Blocks.Count - 1;
        var prefix = bi >= 0 && Blocks[bi].Separator.Length == 0 ? "\n" : string.Empty;
        var blocks = Blocks.ToList(); blocks.Insert(bi + 1, new(ids(), false, [new(ids(), text, AiKnowledgeCorrectionOrigin.Supplement, null, prefix)], "\n"));
        return Restore(Source, Anchors, blocks, token);
    }

    /// <summary>噪声排除保留修订与原文的对应关系，恢复时不重新推导来源。</summary>
    internal AiKnowledgeCorrectionDocument Exclude(Guid blockId, bool excluded, CancellationToken token)
    { var blocks = Blocks.ToArray(); var bi = Index(blockId); blocks[bi] = blocks[bi] with { Excluded = excluded }; return Restore(Source, Anchors, blocks, token); }

    internal AiKnowledgeSourcePosition? Locate(AiKnowledgeCorrectionPart part)
    {
        if (part.AnchorId is not { } id) return null;
        var anchor = Anchors.Single(a => a.Id == id);
        return Source.Blocks[anchor.BlockSequence].Locate(anchor.Offset, anchor.Length);
    }
    private int Index(Guid id) { for (var i = 0; i < Blocks.Count; i++) if (Blocks[i].Id == id) return i; throw Invalid(); }
    private (int Block, int Part) Find(Guid block, Guid part)
    { var bi = Index(block); for (var i = 0; i < Blocks[bi].Parts.Count; i++) if (Blocks[bi].Parts[i].Id == part) return (bi, i); throw Invalid(); }
    private static void ValidateEdit(string text, CancellationToken token)
    { if (text is null || text.Length > MaxEditedCharacters || !AiKnowledgeParsedDocument.ValidText(text, token)) throw Invalid(); }
    private static bool Layout(string value) => value is { Length: <= 64 } && value.All(c => c is '\r' or '\n' or '\t' or ' ');
    private static bool Boundary(string text, int offset) => offset == 0 || offset == text.Length || !(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));
    private static InvalidDataException Invalid() => new("Invalid knowledge correction structure.");
}
