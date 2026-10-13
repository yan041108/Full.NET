using Full.NET.Agents.Runtime;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>一个已结算 DOCX 批次；完整计划不代表全部向量已持久化。</summary>
internal sealed record AiKnowledgeIndexDocxEmbeddingBatch(Guid OperationId, int Ordinal,
    AiKnowledgeIndexDocxPreparation Preparation, IReadOnlyList<ReadOnlyMemory<float>> Vectors);

/// <summary>完整清单冻结后才能预留和派发；只接受规划器读取的原 DOCX。</summary>
internal sealed class AiKnowledgeIndexDocxEmbeddingService(AiKnowledgeIndexDocxPlanningService planning,
    AiKnowledgeIndexDocxManifestStore manifests, AiKnowledgeIndexBudgetService admission,
    IAiOperationBudgetStore budget, IDataTransactionState transactions,
    IEnumerable<IAiModelClientFactory> factories, AiModelBindingScope bindings)
{
    /// <summary>只派发新预留；未知或重复操作交给对账，不重试、不续租、不改为 ready。</summary>
    internal Task<AiKnowledgeIndexDocxEmbeddingBatch?> ExecuteAsync(AiKnowledgeIndexTaskBinding binding,
        Guid taskId, Guid leaseToken, int ordinal, CancellationToken token) =>
        AiKnowledgeTelemetry.RunIndexAsync(taskId, "docx", observation =>
            ExecuteCoreAsync(binding, taskId, leaseToken, ordinal, token, observation), token);

    private async Task<AiKnowledgeIndexDocxEmbeddingBatch?> ExecuteCoreAsync(AiKnowledgeIndexTaskBinding binding,
        Guid taskId, Guid leaseToken, int ordinal, CancellationToken token, AgentExecutionTelemetry.Observation observation)
    {
        Check(token);
        if (ordinal is < 0 or >= 2048) throw new ArgumentOutOfRangeException(nameof(ordinal));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25)); var bounded = deadline.Token;
        var prepared = await planning.PlanAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false);
        if (prepared is null || ordinal >= prepared.Plan.Batches.Count) return null;
        var model = await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, bounded).ConfigureAwait(false);
        if (model is null) return null;
        var factory = factories.SingleOrDefault(f => f.ProviderKey == model.ProviderKey);
        if (factory is null) return null;
        Check(bounded);
        // 完整清单先于操作 ID 与预算提交；任何拒绝都不能触发模型调用。
        if (!await manifests.PersistAsync(binding, taskId, leaseToken, prepared.Plan, bounded).ConfigureAwait(false)) return null;
        model = await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, bounded).ConfigureAwait(false);
        if (model is null) return null;
        var selected = prepared.Plan.Batches[ordinal]; var inputs = selected.Chunks.Select(c => c.Text).ToArray();
        var admitted = await admission.ReserveAsync(binding, taskId, leaseToken, ordinal, inputs, bounded).ConfigureAwait(false);
        // 重复回执也可关联原操作；观测不取得派发或结算所有权。
        if (admitted is not null) observation.SetOperationId(admitted.Request.OperationId);
        if (admitted?.Source is null) return null;
        var receipt = new ChargeReceipt(); var outcome = "failed";
        AiKnowledgeIndexDocxEmbeddingBatch? completed = null;
        try
        {
            Check(bounded);
            if (!AiKnowledgeIndexEmbeddingService.SameSource(prepared.Source, admitted.Source)
                || admitted.Request != AiEmbeddingBudgetRequest.Create(admitted.Request.OperationId, model, inputs)
                || await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, bounded).ConfigureAwait(false) is null)
            { outcome = "cancelled"; return null; }
            IReadOnlyList<ReadOnlyMemory<float>>? copied;
            Check(bounded);
            copied = await ModelAsync(async io =>
            {
                using var generator = await factory.CreateEmbeddingGeneratorAsync(bindings.Create(model), io).ConfigureAwait(false);
                if (await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, io).ConfigureAwait(false) is null)
                    return null;
                Check(io);
                // 从可能计费调用开始保留未知用量；异常绝不推断为零。
                receipt.Begin();
                var generated = await generator.GenerateAsync(Array.AsReadOnly(inputs), cancellationToken: io).ConfigureAwait(false);
                if (generated is null) throw new InvalidDataException("AI embedding response is missing.");
                var reported = new AiOperationUsage(generated.Usage?.InputTokenCount, generated.Usage?.OutputTokenCount ?? 0);
                try { AiExecutionBudget.ValidateUsage(reported); }
                catch (AiBudgetException) { throw new InvalidDataException("AI embedding usage is invalid."); }
                receipt.Report(reported);
                if (reported.InputTokens > admitted.Request.InputTokenLimit || reported.OutputTokens > 0)
                    throw new InvalidDataException("AI embedding usage exceeded the admitted bounds.");
                return CopyVectors(generated, inputs.Length, model.ModelId == "text-embedding-3-small" ? 1536 : 3072, io);
            }, bounded).ConfigureAwait(false);
            if (copied is null) { outcome = "cancelled"; return null; }
            if (await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, bounded).ConfigureAwait(false) is null)
            { outcome = "cancelled"; return null; }
            Check(bounded); outcome = "succeeded";
            completed = new(admitted.Request.OperationId, ordinal, prepared, copied);
        }
        catch (OperationCanceledException) { outcome = "cancelled"; throw; }
        finally
        {
            // 原范围账本独立结算；等待失败必须传播，不能交付未结算向量。
            var settledUsage = receipt.Freeze();
            var settling = AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async _ => { await budget.SettleAsync(admitted.Request.OperationId, settledUsage, outcome, CancellationToken.None).ConfigureAwait(false); return true; }, CancellationToken.None);
            try { await settling.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { _ = ObserveAsync(settling); throw new IOException("DOCX budget settlement was not confirmed."); }
        }
        Check(bounded);
        return await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, prepared, bounded).ConfigureAwait(false) is not null
            ? completed : null;
    }

    // 所有权留在工作任务中；取消回调不能阻塞调用方，晚到结果不允许交付。
    private async Task<T> ModelAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        Check(token); var io = new CancellationTokenSource(); var accepted = false;
        var work = AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(_ => { Check(io.Token); return action(io.Token); }, CancellationToken.None);
        try { var result = await work.WaitAsync(token).ConfigureAwait(false); Check(token); accepted = true; return result; }
        finally
        {
            if (accepted) io.Dispose();
            else
            {
                var cleanup = Task.WhenAll(ObserveAsync(io.CancelAsync()), ObserveAsync(work));
                try { await cleanup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    throw new IOException("DOCX model cleanup was not confirmed.");
                }
                finally { if (cleanup.IsCompleted) io.Dispose(); else _ = DisposeAfterAsync(cleanup, io); }
            }
        }
    }

    private static async Task DisposeAfterAsync(Task work, CancellationTokenSource io)
    { await ObserveAsync(work).ConfigureAwait(false); io.Dispose(); }
    private static async Task ObserveAsync(Task work)
    { try { await work.ConfigureAwait(false); } catch { } }

    // 清理状态不改变收费阶段；一次冻结后迟到工作不能写回计量或开始新计费调用。
    private sealed class ChargeReceipt
    {
        private readonly object gate = new();
        private AiOperationUsage usage = new(0, 0);
        private bool frozen;
        internal void Begin()
        {
            lock (gate)
            {
                if (frozen) throw new OperationCanceledException("DOCX budget receipt is already frozen.");
                usage = new(null, null);
            }
        }
        internal void Report(AiOperationUsage reported)
        { lock (gate) { if (!frozen) usage = reported; } }
        internal AiOperationUsage Freeze()
        { lock (gate) { frozen = true; return usage; } }
    }

    private static IReadOnlyList<ReadOnlyMemory<float>> CopyVectors(GeneratedEmbeddings<Embedding<float>> generated,
        int count, int dimensions, CancellationToken token)
    {
        if (generated.Count != count) throw new InvalidDataException("AI embedding count is invalid.");
        var copied = new ReadOnlyMemory<float>[count];
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (generated[index].Vector.Length != dimensions) throw new InvalidDataException("AI embedding dimensions are invalid.");
            var vector = generated[index].Vector.ToArray(); double norm = 0;
            foreach (var value in vector)
            { if (!float.IsFinite(value)) throw new InvalidDataException("AI embedding values are invalid."); norm += (double)value * value; }
            if (norm <= 0) throw new InvalidDataException("AI embedding values are invalid."); copied[index] = vector;
        }
        return Array.AsReadOnly(copied);
    }

    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("DOCX embedding cannot hold an ambient transaction."); }
}
