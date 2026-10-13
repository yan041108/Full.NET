using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>预算准入结果不是完整派发授权；空来源表示已存在的操作只能对账，禁止自动再次调用。</summary>
internal sealed record AiKnowledgeIndexBudgetAdmission(AiOperationRequest Request, AiOperationReservation Reservation, AiKnowledgeIndexDispatchRecord? Source);

/// <summary>先固化操作 ID，再预留；不拥有外部调用，也不推断未知操作是否已经执行。</summary>
internal sealed class AiKnowledgeIndexBudgetService(IQueryExecutor query, AiKnowledgeIndexTaskStore tasks,
    AiKnowledgeIndexBatchStore batches, IAiOperationBudgetStore budget)
{
    /// <summary>先固化请求身份再预留预算；来源为空的重复凭据只能对账，不能重新派发。</summary>
    /// <remarks>此方法不调用模型，须在无外层事务的作用域调用，确保批次先提交及账本使用自有短事务。新预留后的失败可以明确结算为零；预留响应未知时不推断是否记账。真实调用消费者须校验输入来源、执行者和数据去向，派发前再次复核，并独立结算实际或未知用量。</remarks>
    internal async Task<AiKnowledgeIndexBudgetAdmission?> ReserveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, int batchOrdinal, IReadOnlyList<string> inputs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(inputs);
        if (batchOrdinal is < 0 or > 4095 || AiEmbeddingContentPolicy.ValidateInputs(inputs) is not null)
            throw new ArgumentException("Invalid knowledge embedding batch.", nameof(inputs));
        // 复制后再次验证，后续指纹不依赖调用方可变集合；不裁剪或重写原文。
        var frozen = inputs.ToArray();
        if (AiEmbeddingContentPolicy.ValidateInputs(frozen) is not null) throw new ArgumentException("Invalid knowledge embedding batch.", nameof(inputs));
        var source = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (source is null || !AiKnowledgePolicy.IsKnownClassification(source.DataClassification)) return null;
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant
                ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", source.ModelConfigId)), token).ConfigureAwait(false);
        if (model is null || !model.IsEnabled || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
            || source.ModelProviderKey != model.ProviderKey || source.ModelId != model.ModelId
            || source.ModelEndpointBaseUrl != model.EndpointBaseUrl || source.ModelOrganizationId != model.OrganizationId) return null;
        // Token 门禁早于任何批次身份或预算写入；历史指纹仍使用原字节上界，不使旧索引失效。
        if (AiKnowledgeEmbeddingInputPolicy.Validate(model, frozen, token) is not null)
            throw new InvalidDataException("Knowledge embedding input exceeds the supported boundary.");
        // OperationId 不参与请求指纹；临时使用任务 ID 构造完整请求，持久化后替换为正式批次 ID。
        var draft = AiEmbeddingBudgetRequest.Create(taskId, model, frozen);
        var batch = await batches.PrepareAsync(binding, taskId, leaseToken, batchOrdinal, AiOperationBudgetStore.Fingerprint(draft), frozen.Length, token).ConfigureAwait(false);
        if (batch is null) return null;
        var request = draft with { OperationId = batch.Id };
        var reservation = await budget.ReserveAsync(request, token).ConfigureAwait(false);
        if (reservation.OperationId != request.OperationId) throw new InvalidDataException("Unexpected AI budget operation identity.");
        // 重复或 unknown 的既有记录不能从租约接替推断为未执行，也不能释放为零。
        if (!reservation.IsNew) return new(request, reservation, null);
        if (reservation.UsageStatus != "reserved") throw new InvalidDataException("Unexpected new AI budget usage status.");
        var admitted = false;
        try
        {
            var current = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
            // 不仅比较版本：等待期间同版本分类、Provider、端点或组织变化也不能返回派发来源。
            if (current is null || !AiKnowledgeIndexEmbeddingService.SameSource(source, current)) return null;
            admitted = true;
            return new(request, reservation, current);
        }
        finally
        {
            // 尚未调用模型的路径有确定零用量；取消不阻止独立结算，结算失败必须传播。
            if (!admitted) await budget.SettleAsync(request.OperationId, new(0, 0), "cancelled", CancellationToken.None).ConfigureAwait(false);
        }
    }
}
