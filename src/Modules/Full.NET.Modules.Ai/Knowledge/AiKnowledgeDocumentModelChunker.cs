using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>一个原始逻辑块内的原文区间；来源坐标与正文同时冻结。</summary>
internal sealed record AiKnowledgeDocumentChunk(int Sequence, int BlockSequence, string Text, int TokenCount, AiKnowledgeSourcePosition Position)
{
    internal int Offset => Position.Offset;
    internal int Length => Position.Length;
}
/// <summary>统一解析文档与精确批准模型的本地分块；不表示费用结算、持久化或发布完成。</summary>
internal sealed record AiKnowledgeDocumentModelChunks(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, AiKnowledgeParsedDocument Document,
    string TokenizerVersion, AiKnowledgeChunkOptions Options, IReadOnlyList<AiKnowledgeDocumentChunk> Chunks)
{
    // 由拥有隔离进程的规划入口填入实际受检镜像；本地纯分块不授予运行来源身份。
    internal string ParserRuntimeId { get; init; } = string.Empty;
    internal string DataClassification { get; init; } = string.Empty;
    internal string DestinationHash { get; init; } = string.Empty;
}
/// <summary>任何逻辑块或整体预算失败均不交付部分内容。</summary>
internal sealed record AiKnowledgeDocumentModelChunkResult(AiKnowledgeDocumentModelChunks? Output,
    AiKnowledgeModelChunkFailure Failure, AiKnowledgeChunkFailure ChunkFailure = AiKnowledgeChunkFailure.None)
{
    internal bool IsSuccess => Output is not null;
}

/// <summary>所有结构来源共享完整快照和全局预算；不把单元格、幻灯片或图片框变成虚构页码。</summary>
internal static class AiKnowledgeDocumentModelChunker
{
    internal static AiKnowledgeDocumentModelChunkResult Chunk(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedDocument document, AiKnowledgeChunkOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(knowledge); ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(options);
        var frozen = AiKnowledgeParsedDocument.FromBlocks(document.Format, document.ParserVersion, document.ContentHash, document.SizeBytes, document.Blocks, token, document.Policy);
        if (frozen is null) return Rejected(AiKnowledgeChunkFailure.InvalidDocument);
        var approval = AiKnowledgeModelChunker.ValidateApproval(knowledge, model);
        if (approval != AiKnowledgeModelChunkFailure.None) return new(null, approval);
        // 识别不确定性不能被 Token 分块洗成确定正文；解析快照仍可供维护者校对。
        if (frozen.Blocks.Any(b => b.Position.Image is { Confidence: < 0.8 })) return Rejected(AiKnowledgeChunkFailure.UncertainSource);
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token); var chunks = new List<AiKnowledgeDocumentChunk>();
        var tokens = 0; var characters = 0;
        foreach (var block in frozen.Blocks)
        {
            token.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(block.Text)) continue;
            if (chunks.Count > 0 && chunks.Count >= options.MaxChunks) return Rejected(AiKnowledgeChunkFailure.TooManyChunks);
            if (chunks.Count > 0 && tokens >= options.MaxTotalTokens) return Rejected(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded);
            var remaining = chunks.Count == 0 ? options : options with { MaxChunks = options.MaxChunks - chunks.Count, MaxTotalTokens = options.MaxTotalTokens - tokens };
            var bytes = new UTF8Encoding(false, true).GetBytes(block.Text); AiKnowledgeChunkResult result;
            try
            {
                result = AiKnowledgeTextChunker.Chunk(new(block.Text, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length),
                    tokenizer, AiKnowledgeCl100kTokenizer.Version, remaining, token);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            if (!result.IsSuccess) return Rejected(result.Failure);
            foreach (var chunk in result.Output!.Chunks)
            {
                token.ThrowIfCancellationRequested();
                if (chunk.Length > AiKnowledgeTextChunker.MaxTotalCharacters - characters || AiEmbeddingContentPolicy.ValidateInputs([chunk.Text]) is not null)
                    return Rejected(AiKnowledgeChunkFailure.OutputTooLarge);
                var p = block.Locate(chunk.Offset, chunk.Length); if (p is null) return Rejected(AiKnowledgeChunkFailure.InvalidDocument);
                tokens += chunk.TokenCount; characters += chunk.Length;
                chunks.Add(new(chunks.Count, block.Sequence, chunk.Text, chunk.TokenCount, p));
            }
        }
        token.ThrowIfCancellationRequested();
        return new(new(knowledge.Id, knowledge.Version, knowledge.TenantId, model.Id, model.Version, model.ProviderKey, model.ModelId,
            frozen, AiKnowledgeCl100kTokenizer.Version, options, Array.AsReadOnly(chunks.ToArray()))
            { DataClassification = knowledge.DataClassification, DestinationHash = Destination(model) }, AiKnowledgeModelChunkFailure.None);
    }

    /// <summary>不含凭据的实际目的地指纹；配置版本未变也不能在恢复批次时漂移端点或组织。</summary>
    internal static string Destination(AiModelConfigRecord model)
        => Destination(model.EndpointBaseUrl, model.OrganizationId);

    internal static string Destination(string endpoint, string? organizationId)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true));
        writer.Write(endpoint); writer.Write(organizationId is not null);
        if (organizationId is { } organization) writer.Write(organization);
        writer.Flush(); return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }

    private static AiKnowledgeDocumentModelChunkResult Rejected(AiKnowledgeChunkFailure failure)
        => new(null, AiKnowledgeModelChunkFailure.ChunkingFailed, failure);
}
