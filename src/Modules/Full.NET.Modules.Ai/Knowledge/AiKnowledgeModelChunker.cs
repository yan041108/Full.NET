using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部绑定失败，不作为 HTTP 或持久化协议。</summary>
internal enum AiKnowledgeModelChunkFailure { None, ModelNotApproved, ModelScopeMismatch, UnsupportedModel, ChunkingFailed }
/// <summary>完整分块绑定实际配置及权威知识库版本，不携带凭据。</summary>
internal sealed record AiKnowledgeModelChunks(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, AiKnowledgeStructuredChunks Chunks);
/// <summary>失败不提供部分内容。</summary>
internal sealed record AiKnowledgeModelChunkResult(AiKnowledgeModelChunks? Output, AiKnowledgeModelChunkFailure Failure,
    AiKnowledgeChunkFailure ChunkFailure = AiKnowledgeChunkFailure.None)
{
    internal bool IsSuccess => Output is not null;
}
/// <summary>只对精确获批配置计费，不授权模型外发。</summary>
internal static class AiKnowledgeModelChunker
{
    /// <summary>用已授权的权威记录复核精确批准，再调用固定词表分块；不访问数据库、网络或凭据。</summary>
    /// <remarks>此本地检查不代替 Files 授权或派发前权威复核。配置版本冻结目的地，词表支持也不表示模型可用或外发获准。</remarks>
    /// <param name="knowledge">来自可信作用域及授权查询的知识库，输出固化其审批版本以供后续复核。</param>
    /// <param name="model">同一可信作用域内可用配置；Host 配置可被租户显式批准，租户配置不能跨范围。</param>
    /// <param name="document">已经授权、摘要绑定的来源原文；本方法不授予来源读取资格。</param>
    /// <param name="format">权威文件版本中的明确格式，不从正文推断。</param>
    /// <param name="options">沿用完整正文与重复上下文的 Token、块数及输出预算。</param>
    /// <param name="token">在可信同步词表初始化及逐块计数前后观察取消，不能强制终止同步 SDK。</param>
    internal static AiKnowledgeModelChunkResult Chunk(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedText document, AiKnowledgeTextFormat format, AiKnowledgeChunkOptions options, CancellationToken token)
        => AiKnowledgeTelemetry.Run("knowledge_chunk_text", () => ChunkCore(knowledge, model, document, format, options, token), token);

    /// <summary>整份 PDF 在外层观测一次；页内复用原检查，避免重复记录为普通文本执行。</summary>
    internal static AiKnowledgeModelChunkResult ChunkCore(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedText document, AiKnowledgeTextFormat format, AiKnowledgeChunkOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(model);
        // 精确批准绑定配置版本，不能由默认模型、显示名称或相同 Provider 替代。
        if (knowledge.Id == Guid.Empty || knowledge.Version <= 0 || !model.IsEnabled
            || !AiKnowledgePolicy.IsKnownClassification(knowledge.DataClassification)
            || !AiKnowledgePolicy.Allows(knowledge.IsEnabled, knowledge.EmbeddingModelConfigId,
                knowledge.EmbeddingModelVersion, model.Id, model.Version))
            return new(null, AiKnowledgeModelChunkFailure.ModelNotApproved);
        if (knowledge.TenantId == Guid.Empty || model.TenantId == Guid.Empty
            || (model.TenantId.HasValue && model.TenantId != knowledge.TenantId))
            return new(null, AiKnowledgeModelChunkFailure.ModelScopeMismatch);
        // 兼容协议不证明实际模型身份；首批只核准官方目的地和精确模型名，其他绑定必须另有真实词表证据。
        if (model.ProviderKey != AiProviderKeys.OpenAiCompatible
            || model.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || !OfficialDestination(model.EndpointBaseUrl))
            return new(null, AiKnowledgeModelChunkFailure.UnsupportedModel);
        // PDF 页由外层完整快照保留页坐标；普通文本使用同一来源边界，原结构分块继续决定格式失败分类。
        ArgumentNullException.ThrowIfNull(document);
        if (document.Source != AiKnowledgeTextSource.PdfPage && format is AiKnowledgeTextFormat.PlainText or AiKnowledgeTextFormat.Markdown)
        {
            var parsed = AiKnowledgeParsedDocument.FromText(document, format, token);
            if (parsed is null) return new(null, AiKnowledgeModelChunkFailure.ChunkingFailed, AiKnowledgeChunkFailure.InvalidDocument);
            document = new(parsed.Blocks[0].Text, document.ContentHash, parsed.SizeBytes);
        }
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token);
        var result = AiKnowledgeStructuredChunker.Chunk(document, tokenizer, AiKnowledgeCl100kTokenizer.Version, format, options, token);
        if (!result.IsSuccess) return new(null, AiKnowledgeModelChunkFailure.ChunkingFailed, result.Failure);
        token.ThrowIfCancellationRequested();
        return new(new(knowledge.Id, knowledge.Version, knowledge.TenantId, model.Id, model.Version,
            model.ProviderKey, model.ModelId, result.Output!), AiKnowledgeModelChunkFailure.None);
    }

    /// <summary>只识别固定词表已核准的官方目的地形式，不证明网络许可或模型可用。</summary>
    internal static bool OfficialDestination(string endpoint)
    {
        // 在 URI 规范化之前约束原始形式，避免百分号编码、点段或反斜杠被转换为已核准路径。
        // 主机/协议大小写和显式默认端口可等价，路径必须精确；网络政策仍由 Provider 派发边界负责。
        const string origin = "https://api.openai.com";
        return endpoint is not null && endpoint.StartsWith(origin, StringComparison.OrdinalIgnoreCase)
            && endpoint.AsSpan(origin.Length) is "/v1" or "/v1/" or ":443/v1" or ":443/v1/";
    }
}
