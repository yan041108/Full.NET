using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>冻结不含凭据的批准模型及目的地；持久身份本身不能代替当前授权。</summary>
internal sealed record AiKnowledgeCorrectionModelBinding(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, string DataClassification, string DestinationHash)
{
    internal static AiKnowledgeCorrectionModelBinding? Capture(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model) =>
        AiKnowledgeModelChunker.ValidateApproval(knowledge, model) == AiKnowledgeModelChunkFailure.None
            ? new(knowledge.Id, knowledge.Version, knowledge.TenantId, model.Id, model.Version, model.ProviderKey, model.ModelId,
                knowledge.DataClassification, AiKnowledgeDocumentModelChunker.Destination(model)) : null;
    internal bool Valid => KnowledgeBaseId != Guid.Empty && KnowledgeBaseVersion > 0 && TenantId != Guid.Empty
        && ModelConfigId != Guid.Empty && ModelConfigVersion > 0 && ProviderKey == "openai_compatible"
        && ModelId is "text-embedding-3-small" or "text-embedding-3-large"
        && AiKnowledgePolicy.IsKnownClassification(DataClassification) && Hash(DestinationHash);
    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>编辑片段在一个模型输入中的交集；更正只指向完整原始上下文，补写没有位置。</summary>
internal sealed record AiKnowledgeCorrectionChunkReference(Guid PartId, AiKnowledgeCorrectionOrigin Origin,
    int ChunkOffset, int PartOffset, int Length, AiKnowledgeSourcePosition? Position);
/// <summary>分块保留全部来源集合，不能把跨页合并压缩成第一个位置。</summary>
internal sealed record AiKnowledgeCorrectionIndexChunk(int Sequence, Guid BlockId, int Offset, string Text, int TokenCount,
    IReadOnlyList<AiKnowledgeCorrectionChunkReference> References);
/// <summary>完整人工计划中的有界模型输入；摘要不授予费用或发布资格。</summary>
internal sealed record AiKnowledgeCorrectionIndexBatch(int Ordinal, string SourceHash, IReadOnlyList<AiKnowledgeCorrectionIndexChunk> Chunks);

/// <summary>由冻结人工修订生成完整计划；人工排除、分块边界和全部原文证据一起进入摘要。</summary>
internal sealed class AiKnowledgeCorrectionIndexPlan
{
    internal const string FormatKey = "knowledge-correction-plan-v1";
    internal AiKnowledgeCorrectionSnapshot Snapshot { get; }
    internal AiKnowledgeCorrectionModelBinding Model { get; }
    internal IReadOnlyList<AiKnowledgeCorrectionIndexChunk> Chunks { get; }
    internal IReadOnlyList<AiKnowledgeCorrectionIndexBatch> Batches { get; }
    internal string PlanHash { get; }
    internal long TokenCount { get; }
    private AiKnowledgeCorrectionIndexPlan(AiKnowledgeCorrectionSnapshot snapshot, AiKnowledgeCorrectionModelBinding model,
        AiKnowledgeCorrectionIndexChunk[] chunks, AiKnowledgeCorrectionIndexBatch[] batches, string hash, long tokens)
    { Snapshot = snapshot; Model = model; Chunks = Array.AsReadOnly(chunks); Batches = Array.AsReadOnly(batches); PlanHash = hash; TokenCount = tokens; }

    /// <summary>完整验收规范快照后重新分块，拒绝空计划、未核对 OCR 和超预算；没有部分成功或静默截断。</summary>
    internal static AiKnowledgeCorrectionIndexPlan? Create(AiKnowledgeCorrectionSnapshot snapshot,
        AiKnowledgeCorrectionModelBinding model, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(model);
        var id = snapshot.Identity;
        if (!model.Valid || id.SourceId == Guid.Empty || id.RevisionId is not { } revision || revision == Guid.Empty || id.HeadVersion <= 0
            || !AiKnowledgeCorrectionModelBinding.Hash(id.SourceHash) || !AiKnowledgeCorrectionModelBinding.Hash(id.PayloadHash)) return null;
        var original = snapshot.Document.Source;
        var source = AiKnowledgeParsedDocument.FromBlocks(original.Format, original.ParserVersion, original.ContentHash, original.SizeBytes,
            original.Blocks, token, original.Policy);
        if (source is null || AiKnowledgeCorrectionWireCodec.SourceHash(source, snapshot.ParserRuntimeId, token) != id.SourceHash) return null;
        var corrected = AiKnowledgeCorrectionDocument.Restore(source, snapshot.Document.Anchors, snapshot.Document.Blocks, token);
        var wire = AiKnowledgeCorrectionWireCodec.Encode(corrected, snapshot.ParserRuntimeId, token);
        try { if (Convert.ToHexStringLower(SHA256.HashData(wire)) != id.PayloadHash) return null; }
        finally { CryptographicOperations.ZeroMemory(wire); }
        var frozen = snapshot with { Document = corrected }; var anchors = corrected.Anchors.ToDictionary(a => a.Id);
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token); var options = new AiKnowledgeChunkOptions();
        var chunks = new List<AiKnowledgeCorrectionIndexChunk>(); long tokens = 0; var characters = 0;
        foreach (var block in corrected.Blocks)
        {
            token.ThrowIfCancellationRequested(); if (block.Excluded) continue;
            var text = new StringBuilder(); var spans = new List<(AiKnowledgeCorrectionPart Part, int Start)>();
            foreach (var part in block.Parts)
            {
                if (part.Origin == AiKnowledgeCorrectionOrigin.Original && part.AnchorId is { } a
                    && source.Blocks[anchors[a].BlockSequence].Position.Image is { Confidence: < 0.8 }) return null;
                text.Append(part.Prefix); spans.Add((part, text.Length)); text.Append(part.Text);
            }
            var body = text.ToString(); if (string.IsNullOrWhiteSpace(body)) continue;
            if (chunks.Count >= options.MaxChunks || tokens >= options.MaxTotalTokens) return null;
            var bytes = Encoding.UTF8.GetBytes(body); AiKnowledgeChunkResult result;
            try
            {
                result = AiKnowledgeTextChunker.Chunk(new(body, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length), tokenizer,
                    AiKnowledgeCl100kTokenizer.Version, options with { MaxChunks = options.MaxChunks - chunks.Count, MaxTotalTokens = options.MaxTotalTokens - (int)tokens }, token);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            if (!result.IsSuccess) return null;
            foreach (var chunk in result.Output!.Chunks)
            {
                token.ThrowIfCancellationRequested();
                if (chunk.Length > AiKnowledgeTextChunker.MaxTotalCharacters - characters || AiEmbeddingContentPolicy.ValidateInputs([chunk.Text]) is not null) return null;
                var references = new List<AiKnowledgeCorrectionChunkReference>();
                foreach (var (part, start) in spans)
                {
                    var begin = Math.Max(start, chunk.Offset); var end = Math.Min(start + part.Text.Length, chunk.Offset + chunk.Length);
                    if (begin >= end) continue;
                    AiKnowledgeSourcePosition? position = null;
                    if (part.AnchorId is { } anchorId)
                    {
                        var anchor = anchors[anchorId]; var raw = source.Blocks[anchor.BlockSequence];
                        // 只有原文字面片段可缩小真实区间；更正沿用完整上下文，不能按编辑长度换算。
                        position = part.Origin == AiKnowledgeCorrectionOrigin.Original
                            ? raw.Locate(anchor.Offset + begin - start, end - begin) : raw.Locate(anchor.Offset, anchor.Length);
                        if (position is null) return null;
                    }
                    references.Add(new(part.Id, part.Origin, begin - chunk.Offset, begin - start, end - begin, position));
                }
                chunks.Add(new(chunks.Count, block.Id, chunk.Offset, chunk.Text, chunk.TokenCount, Array.AsReadOnly(references.ToArray())));
                tokens += chunk.TokenCount; characters += chunk.Length;
            }
        }
        if (chunks.Count == 0) return null;
        var batches = new List<AiKnowledgeCorrectionIndexBatch>(); var cursor = 0;
        while (cursor < chunks.Count)
        {
            token.ThrowIfCancellationRequested(); var start = cursor; var count = 0;
            while (cursor < chunks.Count && cursor - start < AiEmbeddingContentPolicy.MaxBatchSize
                && chunks[cursor].Text.Length <= AiEmbeddingContentPolicy.MaxTotalCharacters - count)
            { count += chunks[cursor].Text.Length; cursor++; }
            if (cursor == start) return null;
            var selected = Array.AsReadOnly(chunks.GetRange(start, cursor - start).ToArray()); var ordinal = batches.Count;
            var hash = Digest(writer =>
            {
                Header(writer, frozen, model); writer.Write(ordinal); writer.Write(selected.Count);
                foreach (var chunk in selected)
                { writer.Write(chunk.Sequence); writer.Write(chunk.BlockId.ToString("N")); writer.Write(chunk.Offset); writer.Write(chunk.Text); writer.Write(chunk.TokenCount); }
            });
            batches.Add(new(ordinal, hash, selected));
        }
        var planHash = Digest(writer =>
        {
            Header(writer, frozen, model); writer.Write(chunks.Count); writer.Write(tokens); writer.Write(batches.Count);
            foreach (var batch in batches) { writer.Write(batch.Ordinal); writer.Write(batch.SourceHash); }
        });
        token.ThrowIfCancellationRequested(); return new(frozen, model, chunks.ToArray(), batches.ToArray(), planHash, tokens);
    }

    private static void Header(BinaryWriter writer, AiKnowledgeCorrectionSnapshot snapshot, AiKnowledgeCorrectionModelBinding model)
    {
        writer.Write(FormatKey); writer.Write(snapshot.Identity.SourceId.ToString("N")); writer.Write(snapshot.Identity.RevisionId!.Value.ToString("N"));
        writer.Write(snapshot.Identity.HeadVersion); writer.Write(snapshot.Identity.SourceHash); writer.Write(snapshot.Identity.PayloadHash); writer.Write(snapshot.ParserRuntimeId);
        writer.Write(model.KnowledgeBaseId.ToString("N")); writer.Write(model.KnowledgeBaseVersion); writer.Write(model.TenantId?.ToString("N") ?? "host");
        writer.Write(model.ModelConfigId.ToString("N")); writer.Write(model.ModelConfigVersion); writer.Write(model.ProviderKey); writer.Write(model.ModelId);
        writer.Write(model.DataClassification); writer.Write(model.DestinationHash); writer.Write(AiKnowledgeCl100kTokenizer.Version);
        var options = new AiKnowledgeChunkOptions(); writer.Write(options.MaxTokens); writer.Write(options.OverlapTokens); writer.Write(options.MaxChunks); writer.Write(options.MaxTotalTokens);
    }
    private static string Digest(Action<BinaryWriter> action)
    {
        using var stream = new MemoryStream();
        try
        {
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true)) { action(writer); writer.Flush(); }
            return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
        }
        finally { CryptographicOperations.ZeroMemory(stream.GetBuffer()); }
    }
}
