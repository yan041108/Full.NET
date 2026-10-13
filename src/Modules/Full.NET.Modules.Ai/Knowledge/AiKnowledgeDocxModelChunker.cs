using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>一个逻辑段落或单元格内的完整原文区间；绝不跨逻辑块重叠。</summary>
internal sealed record AiKnowledgeDocxChunk(int Sequence, int BlockSequence, int Offset, int Length, string Text, int TokenCount);
/// <summary>冻结获批模型身份及整个逻辑文档；不包含凭据或付费证明。</summary>
internal sealed record AiKnowledgeDocxModelChunks(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, AiKnowledgeParsedDocx Document,
    string TokenizerVersion, AiKnowledgeChunkOptions Options, IReadOnlyList<AiKnowledgeDocxChunk> Chunks);
/// <summary>任何块或预算失败均不提供部分结果。</summary>
internal sealed record AiKnowledgeDocxModelChunkResult(AiKnowledgeDocxModelChunks? Output,
    AiKnowledgeModelChunkFailure Failure, AiKnowledgeChunkFailure ChunkFailure = AiKnowledgeChunkFailure.None)
{
    internal bool IsSuccess => Output is not null;
}
/// <summary>使用获批模型真实词表生成本地分块，后续消费者仍须复核权威授权。</summary>
internal static class AiKnowledgeDocxModelChunker
{
    internal static AiKnowledgeDocxModelChunkResult Chunk(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedDocx document, AiKnowledgeChunkOptions options, CancellationToken token)
        => AiKnowledgeTelemetry.Run("knowledge_chunk_docx", () => ChunkCore(knowledge, model, document, options, token), token);

    /// <summary>复用既有完整结构和批准检查，完成只表示本地分块返回。</summary>
    private static AiKnowledgeDocxModelChunkResult ChunkCore(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedDocx document, AiKnowledgeChunkOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(knowledge); ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(options);
        var frozen = AiKnowledgeDocxStructure.Freeze(document, token);
        if (frozen is null) return Rejected(AiKnowledgeChunkFailure.InvalidDocument);
        if (knowledge.Id == Guid.Empty || knowledge.Version <= 0 || !model.IsEnabled
            || !AiKnowledgePolicy.IsKnownClassification(knowledge.DataClassification)
            || !AiKnowledgePolicy.Allows(knowledge.IsEnabled, knowledge.EmbeddingModelConfigId, knowledge.EmbeddingModelVersion, model.Id, model.Version))
            return new(null, AiKnowledgeModelChunkFailure.ModelNotApproved);
        if (knowledge.TenantId == Guid.Empty || model.TenantId == Guid.Empty || model.TenantId.HasValue && model.TenantId != knowledge.TenantId)
            return new(null, AiKnowledgeModelChunkFailure.ModelScopeMismatch);
        const string origin = "https://api.openai.com";
        if (model.ProviderKey != AiProviderKeys.OpenAiCompatible || model.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || model.EndpointBaseUrl is null || !model.EndpointBaseUrl.StartsWith(origin, StringComparison.OrdinalIgnoreCase)
            || model.EndpointBaseUrl.AsSpan(origin.Length) is not ("/v1" or "/v1/" or ":443/v1" or ":443/v1/"))
            return new(null, AiKnowledgeModelChunkFailure.UnsupportedModel);
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token); var chunks = new List<AiKnowledgeDocxChunk>(); var tokens = 0; var characters = 0;
        foreach (var block in frozen.Blocks)
        {
            token.ThrowIfCancellationRequested(); var text = frozen.Text.Substring(block.Offset, block.Length);
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (chunks.Count > 0 && chunks.Count >= options.MaxChunks) return Rejected(AiKnowledgeChunkFailure.TooManyChunks);
            if (chunks.Count > 0 && tokens >= options.MaxTotalTokens) return Rejected(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded);
            var remaining = chunks.Count == 0 ? options : options with { MaxChunks = options.MaxChunks - chunks.Count, MaxTotalTokens = options.MaxTotalTokens - tokens };
            // 低层输入是本地 UTF-8 文本片段，其摘要和大小只绑定该片段；DOCX 原始来源及坐标单独保存在完整输出。
            var bytes = new UTF8Encoding(false, true).GetBytes(text); AiKnowledgeChunkResult result;
            try { result = AiKnowledgeTextChunker.Chunk(new(text, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length), tokenizer, AiKnowledgeCl100kTokenizer.Version, remaining, token); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            if (!result.IsSuccess) return Rejected(result.Failure);
            foreach (var chunk in result.Output!.Chunks)
            {
                token.ThrowIfCancellationRequested();
                if (chunk.Length > AiKnowledgeTextChunker.MaxTotalCharacters - characters || AiEmbeddingContentPolicy.ValidateInputs([chunk.Text]) is not null)
                    return Rejected(AiKnowledgeChunkFailure.OutputTooLarge);
                tokens += chunk.TokenCount; characters += chunk.Length;
                chunks.Add(new(chunks.Count, block.Sequence, block.Offset + chunk.Offset, chunk.Length, chunk.Text, chunk.TokenCount));
            }
        }
        token.ThrowIfCancellationRequested();
        return new(new(knowledge.Id, knowledge.Version, knowledge.TenantId, model.Id, model.Version, model.ProviderKey, model.ModelId,
            frozen, AiKnowledgeCl100kTokenizer.Version, options, Array.AsReadOnly(chunks.ToArray())), AiKnowledgeModelChunkFailure.None);
    }
    private static AiKnowledgeDocxModelChunkResult Rejected(AiKnowledgeChunkFailure failure) => new(null, AiKnowledgeModelChunkFailure.ChunkingFailed, failure);
}
