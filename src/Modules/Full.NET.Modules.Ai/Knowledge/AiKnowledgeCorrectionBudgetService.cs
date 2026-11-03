using System.Security.Cryptography;
using System.Text;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>人工输入指纹同时绑定完整修订计划和批次；相同文字不能掩盖来源身份改变。</summary>
internal static class AiKnowledgeCorrectionBudgetRequest
{
    internal static AiOperationRequest Create(Guid operation, AiModelConfigRecord model, AiKnowledgeCorrectionIndexPlan plan, int ordinal)
    {
        if (ordinal < 0 || ordinal >= plan.Batches.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
        var batch = plan.Batches[ordinal]; var request = AiEmbeddingBudgetRequest.Create(operation, model, batch.Chunks.Select(c => c.Text).ToArray());
        var bytes = Encoding.UTF8.GetBytes("knowledge-correction-charge-v1\0" + plan.PlanHash + "\0" + batch.SourceHash + "\0" + request.RequestHash);
        try { return request with { RequestHash = Convert.ToHexString(SHA256.HashData(bytes)) }; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

/// <summary>人工批次在完整清单及当前原会话核证后预留；未知或重复回执不授权再次调用模型。</summary>
internal sealed class AiKnowledgeCorrectionBudgetService(AiKnowledgeCorrectionIndexService planning, AiKnowledgeIndexBatchStore batches,
    IAiOperationBudgetStore budget, IDataTransactionState transactions, AiKnowledgeIndexGrantStore? grants = null)
{
    internal async Task<AiKnowledgeIndexBudgetAdmission?> ReserveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeCorrectionIndexPreparation prepared, int ordinal, CancellationToken token)
    {
        Check(token);
        if (ordinal < 0 || ordinal >= prepared.Plan.Batches.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
        var model = await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, token).ConfigureAwait(false);
        if (model is null) return null;
        var inputs = prepared.Plan.Batches[ordinal].Chunks.Select(c => c.Text).ToArray();
        if (AiEmbeddingContentPolicy.ValidateInputs(inputs) is not null || AiKnowledgeEmbeddingInputPolicy.Validate(model, inputs, token) is not null)
            throw new InvalidDataException("Invalid correction embedding input.");
        var draft = AiKnowledgeCorrectionBudgetRequest.Create(taskId, model, prepared.Plan, ordinal);
        var batch = await batches.PrepareAsync(binding, taskId, leaseToken, ordinal, AiOperationBudgetStore.Fingerprint(draft), inputs.Length, token).ConfigureAwait(false);
        Check(token); if (batch is null) return null;
        var request = draft with { OperationId = batch.Id };
        if (grants is not null && !await grants.ReserveBatchAsync(binding, taskId, ordinal, request.InputTokenLimit, token).ConfigureAwait(false)) return null;
        var reservation = await budget.ReserveAsync(request, token).ConfigureAwait(false);
        if (reservation.OperationId != request.OperationId) throw new InvalidDataException("Unexpected correction budget operation.");
        if (!reservation.IsNew) return new(request, reservation, null);
        var admitted = false;
        try
        {
            if (reservation.UsageStatus != "reserved") throw new InvalidDataException("Unexpected correction budget status.");
            Check(token);
            var current = await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, token).ConfigureAwait(false);
            if (current is null || request != AiKnowledgeCorrectionBudgetRequest.Create(batch.Id, current, prepared.Plan, ordinal)) return null;
            admitted = true; return new(request, reservation, prepared.Source);
        }
        finally
        {
            // 尚未调用模型时有确定零用量；已经预留的行不能因撤权或取消遗留为免费重发资格。
            if (!admitted) await budget.SettleAsync(request.OperationId, new(0, 0), "cancelled", CancellationToken.None).ConfigureAwait(false);
        }
    }
    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Correction budget cannot run in an ambient transaction."); }
}
