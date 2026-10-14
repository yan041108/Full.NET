using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>上下文引用和完整召回依赖必须共同保留，不能只按最终引用回查。</summary>
internal sealed record AiKnowledgePreparedAnswer(AiKnowledgeRetrievalSnapshot Retrieval, AiKnowledgeAnswerContext Context);

/// <summary>先准备完整来源依赖，缓冲模型结果并复核后才释放正文；本阶段不派发模型或持久化答案。</summary>
internal sealed class AiKnowledgeAnswerPipeline(AiKnowledgeRetrievalService retrieval, IIdGenerator ids, AiDataProcessingPolicy? dataPolicy = null)
{
    /// <summary>准备有界问答数据包，不派发模型；上下文构造后仍重新核对读取资格。</summary>
    internal Task<AiKnowledgePreparedAnswer> PrepareAsync(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor, AiModelConfigRecord model,
        ReadOnlyMemory<float> vector, string question, CancellationToken token, AiModelConfigRecord? generationModel = null) =>
        AiKnowledgeTelemetry.RunAsync("knowledge_retrieval", () => PrepareCoreAsync(knowledgeBaseIds, actor, model,
            vector, question, token, generationModel), token);

    private async Task<AiKnowledgePreparedAnswer> PrepareCoreAsync(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor, AiModelConfigRecord model,
        ReadOnlyMemory<float> vector, string question, CancellationToken token, AiModelConfigRecord? generationModel = null)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4096) throw new InvalidDataException("问题超过上下文边界。");
        var snapshot = await retrieval.RetrieveAsync(knowledgeBaseIds, actor, model, vector, token, generationModel).ConfigureAwait(false);
        (dataPolicy ?? AiDataProcessingPolicy.Default).Require("context",
            AiDataProcessingPolicy.Strongest(snapshot.Sources.Select(source => source.DataClassification), "restricted"));
        var context = AiKnowledgeAnswerContext.Create(question, snapshot.Chunks, ids, token);
        // 同步词表初始化和上下文构造也可能等待，准备完成前再确认全部依赖。
        await retrieval.RevalidateAsync(snapshot, token).ConfigureAwait(false); return new(snapshot, context);
    }
    /// <summary>仅在严格引用校验与全部来源复核成功后返回正文，拒绝或取消不得泄漏部分答案。</summary>
    internal Task<AiKnowledgeAnswer> AcceptAsync(AiKnowledgePreparedAnswer prepared, ReadOnlyMemory<byte> response, CancellationToken token) =>
        AiKnowledgeTelemetry.RunAsync("knowledge_validation", () => AcceptCoreAsync(prepared, response, token), token);

    private async Task<AiKnowledgeAnswer> AcceptCoreAsync(AiKnowledgePreparedAnswer prepared, ReadOnlyMemory<byte> response, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var answer = prepared.Context.Parse(response, token);
        // 引用子集只决定展示，完整来源快照决定交付资格，未引用的来源也可能影响正文。
        await retrieval.RevalidateAsync(prepared.Retrieval, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); return answer;
    }
}
