using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.AI;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>临时向量绑定原始片段；后续持久化仍须复核代次，不能据此宣告 ready。</summary>
internal sealed record AiKnowledgeEmbeddedChunk(AiKnowledgeStructuredChunk Source, ReadOnlyMemory<float> Vector);
/// <summary>只返回已复核的一个批次；不含凭据，也不改变文档或任务状态。</summary>
internal sealed record AiKnowledgeIndexEmbeddingBatch(Guid OperationId, int BatchOrdinal, AiKnowledgeIndexDispatchRecord Source,
    string ParserVersion, string TokenizerVersion, string ChunkingVersion, AiKnowledgeChunkOptions Options, IReadOnlyList<AiKnowledgeEmbeddedChunk> Chunks)
{
    // 仅供整代次消费者识别预期集合；当前向量载荷 v1 不保存这些临时规划字段。
    internal AiKnowledgeIndexTextPlanSummary? Plan { get; init; }
    internal string BatchSourceHash { get; init; } = "";
}

/// <summary>受会话绑定的内部文本执行器；来源来自 Files，模型来自精确批准，不接受调用方正文。</summary>
internal sealed class AiKnowledgeIndexEmbeddingService(IQueryExecutor query, ICurrentTenant tenant, IDataTransactionState transactions, AiKnowledgeIndexTaskStore tasks,
    AiKnowledgeIndexSessionService sessions, AiKnowledgeIndexBudgetService admission, IAiOperationBudgetStore budget, IBackgroundSessionAuthorization authorization,
    IHostFileReferenceReader references, IHostFileContentReader hostFiles, ITenantResourceFileStore tenantFiles,
    IEnumerable<IAiModelClientFactory> factories, AiModelBindingScope bindings)
{
    private static readonly AiKnowledgeChunkOptions ChunkOptions = new();
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload];

    /// <summary>只派发新预留的精确文本批次；拒绝或失效返回空，取消及基础设施故障传播。</summary>
    /// <remarks>恢复身份只从任务原绑定读取；无外层事务。消费者仍须在写索引前复核，Worker 轮询不由本类负责。</remarks>
    internal async Task<AiKnowledgeIndexEmbeddingBatch?> ExecuteTextBatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, int batchOrdinal, CancellationToken token)
    {
        NoTransaction();
        _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        if (batchOrdinal is < 0 or > 4095) throw new ArgumentOutOfRangeException(nameof(batchOrdinal));
        // 整个文件读取、分块和模型调用有界；不续租、不重试或换模型，超时也不能免费重发。
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25)); var bounded = deadline.Token;
        var session = await sessions.ReadHeldAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false);
        if (session is null) return null;
        var state = await CurrentAsync(binding, taskId, leaseToken, session, bounded).ConfigureAwait(false);
        if (state is null || state.Source.SizeBytes is <= 0 or > AiKnowledgeFilePolicy.MaxBytes
            || state.Source.ContentType is not ("text/plain" or "text/markdown")) return null;
        var factory = factories.SingleOrDefault(item => item.ProviderKey == state.Model.ProviderKey);
        if (factory is null) return null;
        AiKnowledgeModelChunks chunks;
        await using (var stream = await OpenAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false))
        {
            if (stream is null || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return null;
            // 格式来自不可变来源 MIME，固定文件名只用于解析器格式校验，不用 Files 文件名打开路径。
            var parsed = await AiKnowledgeTextParser.ParseAsync(stream, state.Source.ContentType == "text/plain" ? "source.txt" : "source.md",
                state.Source.ContentType, state.Source.ContentHash, bounded).ConfigureAwait(false);
            if (!parsed.IsSuccess || parsed.Document!.SizeBytes != state.Source.SizeBytes) return null;
            var result = AiKnowledgeModelChunker.Chunk(state.Knowledge, state.Model, parsed.Document,
                state.Source.ContentType == "text/plain" ? AiKnowledgeTextFormat.PlainText : AiKnowledgeTextFormat.Markdown, ChunkOptions, bounded);
            if (!result.IsSuccess) return null;
            chunks = result.Output!;
        }
        var plan = AiKnowledgeIndexTextPlan.Create(chunks, bounded);
        if (plan is null || batchOrdinal >= plan.Batches.Count
            || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return null;
        var planned = plan.Batches[batchOrdinal]; var selected = planned.Chunks;
        var inputs = selected.Select(chunk => chunk.EmbeddingText).ToArray();
        var admitted = await admission.ReserveAsync(binding, taskId, leaseToken, batchOrdinal, inputs, bounded).ConfigureAwait(false);
        if (admitted?.Source is null) return null;
        // 从新预留交接开始拥有结算责任；进入 GenerateAsync 后，异常不能推断为零。
        AiOperationUsage usage = new(0, 0); var outcome = "failed";
        AiKnowledgeIndexEmbeddingBatch? completed = null;
        try
        {
            if (!SameSource(state.Source, admitted.Source) || admitted.Request != AiEmbeddingBudgetRequest.Create(admitted.Request.OperationId, state.Model, inputs)
                || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false))
            { outcome = "cancelled"; return null; }
            IReadOnlyList<AiKnowledgeEmbeddedChunk> vectors;
            NoTransaction();
            using (var generator = await factory.CreateEmbeddingGeneratorAsync(bindings.Create(state.Model), bounded).ConfigureAwait(false))
            {
                // 创建客户端或读取凭据也会等待，最终门禁之后才进入可能计费的调用。
                if (!await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false))
                { outcome = "cancelled"; return null; }
                NoTransaction(); bounded.ThrowIfCancellationRequested(); usage = new(null, null);
                var generated = await generator.GenerateAsync(Array.AsReadOnly(inputs), cancellationToken: bounded).ConfigureAwait(false);
                if (generated is null) throw new InvalidDataException("AI embedding response is missing.");
                var reported = new AiOperationUsage(generated.Usage?.InputTokenCount, generated.Usage?.OutputTokenCount ?? 0);
                try { AiExecutionBudget.ValidateUsage(reported); }
                catch (AiBudgetException) { throw new InvalidDataException("AI embedding usage is invalid."); }
                // 先保留已知计量，后续向量错误、取消或撤权不能丢掉已发生的收费事实。
                usage = reported;
                if (reported.InputTokens > admitted.Request.InputTokenLimit || reported.OutputTokens > 0)
                    throw new InvalidDataException("AI embedding usage exceeded the admitted bounds.");
                vectors = CopyVectors(generated, selected, state.Model.ModelId == "text-embedding-3-small" ? 1536 : 3072, bounded);
            }
            bounded.ThrowIfCancellationRequested();
            if (!await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false))
            { outcome = "cancelled"; return null; }
            outcome = "succeeded";
            completed = new(admitted.Request.OperationId, batchOrdinal, state.Source, chunks.Chunks.ParserVersion,
                chunks.Chunks.TokenizerVersion, chunks.Chunks.ChunkingVersion, chunks.Chunks.Options, vectors)
            { Plan = plan.Summary, BatchSourceHash = planned.SourceHash };
        }
        catch (OperationCanceledException) { outcome = "cancelled"; throw; }
        finally
        {
            // 租约/会话失效不阻止原范围账本结算；失败必须传播，不能交付成功向量。
            await budget.SettleAsync(admitted.Request.OperationId, usage, outcome, CancellationToken.None).ConfigureAwait(false);
        }
        // 结算也是异步等待；收费事实已提交后仍可取消、撤权或到期，不能交付等待前的授权结果。
        // 拒绝返回不重写已完成的模型操作计量，也不再次结算或派发。
        bounded.ThrowIfCancellationRequested();
        return await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false) ? completed : null;
    }

    private async Task<State?> CurrentAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, SessionBindingSnapshot session, CancellationToken token)
    {
        NoTransaction();
        if (await sessions.ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false) != session) return null;
        if (!await AuthorizedAsync(binding, session, token).ConfigureAwait(false)) return null;
        var source = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (source is null) return null;
        var knowledge = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(tasks.IsTenant ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost,
            AiSqlParameters.Create(("Id", binding.KnowledgeBaseId), ("OwnerUserId", binding.OwnerUserId)), token).ConfigureAwait(false);
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", source.ModelConfigId)), token).ConfigureAwait(false);
        if (knowledge is null || model is null || knowledge.Id != binding.KnowledgeBaseId || knowledge.OwnerUserId != binding.OwnerUserId
            || knowledge.TenantId != tenant.Id || knowledge.Version != binding.KnowledgeBaseVersion
            || knowledge.DataClassification is not ("public" or "internal" or "restricted") || !model.IsEnabled
            || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion || model.TenantId == Guid.Empty
            || (model.TenantId.HasValue && model.TenantId != tenant.Id)
            || !AiKnowledgePolicy.Allows(knowledge.IsEnabled, knowledge.EmbeddingModelConfigId, knowledge.EmbeddingModelVersion, model.Id, model.Version)) return null;
        // 绑定读取也会等待；其后再授权原会话，不能交付等待前的权限结果。
        if (await sessions.ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false) != session) return null;
        if (!await AuthorizedAsync(binding, session, token).ConfigureAwait(false)) return null;
        // 跨模块授权等待后以 AI 权威查询结束复核；不声称两模块存在分布式原子快照。
        var final = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        return final is not null && SameSource(source, final) ? new(final, knowledge, model) : null;
    }

    private async Task<bool> AuthorizedAsync(AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (session.UserId != binding.OwnerUserId || session.SessionId == Guid.Empty || session.TenantId != tenant.Id) return false;
        foreach (var permission in Permissions)
        {
            var actor = await authorization.AuthorizeAsync(session, permission, token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return false;
        }
        token.ThrowIfCancellationRequested(); return true;
    }

    private async Task<bool> StillCurrentAsync(State expected, AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, SessionBindingSnapshot session, CancellationToken token)
    {
        var current = await CurrentAsync(binding, taskId, leaseToken, session, token).ConfigureAwait(false);
        // 完整模型指纹同时比较实际目的地及选项，而不是仅比较返回行的配置主键。
        return current is not null && SameSource(expected.Source, current.Source)
            && expected.Knowledge.DataClassification == current.Knowledge.DataClassification
            && AiEmbeddingBudgetRequest.Create(taskId, expected.Model, ["binding-check"]) == AiEmbeddingBudgetRequest.Create(taskId, current.Model, ["binding-check"]);
    }

    private async Task<Stream?> OpenAsync(State state, AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, SessionBindingSnapshot session, CancellationToken token)
    {
        NoTransaction(); Stream stream; string contentType;
        if (tasks.IsTenant)
        {
            var opened = await tenantFiles.OpenReadyContentAsync("ai", binding.VersionId, state.Source.FileId, token).ConfigureAwait(false);
            if (!opened.IsSuccess) return null;
            stream = opened.Value!.Content; contentType = opened.Value.ContentType;
        }
        else
        {
            var reference = await references.GetReadyReferenceAsync(state.Source.FileId, token).ConfigureAwait(false);
            if (reference is null || reference.FileId != state.Source.FileId || reference.SizeBytes != state.Source.SizeBytes
                || !string.Equals(reference.ContentHash, state.Source.ContentHash, StringComparison.OrdinalIgnoreCase)
                || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, token).ConfigureAwait(false)) return null;
            NoTransaction();
            var opened = await hostFiles.OpenReadyContentAsync(state.Source.FileId, token).ConfigureAwait(false);
            if (!opened.IsSuccess) return null;
            stream = opened.Value!.Content; contentType = opened.Value.ContentType;
        }
        if (contentType == state.Source.ContentType) return stream;
        await stream.DisposeAsync().ConfigureAwait(false); return null;
    }

    private static IReadOnlyList<AiKnowledgeEmbeddedChunk> CopyVectors(GeneratedEmbeddings<Embedding<float>> generated,
        IReadOnlyList<AiKnowledgeStructuredChunk> chunks, int dimensions, CancellationToken token)
    {
        if (generated.Count != chunks.Count) throw new InvalidDataException("AI embedding count is invalid.");
        var output = new AiKnowledgeEmbeddedChunk[chunks.Count];
        for (var index = 0; index < output.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (generated[index].Vector.Length != dimensions) throw new InvalidDataException("AI embedding dimensions are invalid.");
            var vector = generated[index].Vector.ToArray(); double norm = 0;
            foreach (var value in vector)
            { if (!float.IsFinite(value)) throw new InvalidDataException("AI embedding values are invalid."); norm += (double)value * value; }
            if (norm <= 0) throw new InvalidDataException("AI embedding values are invalid.");
            output[index] = new(chunks[index], vector);
        }
        return Array.AsReadOnly(output);
    }

    internal static bool SameSource(AiKnowledgeIndexDispatchRecord expected, AiKnowledgeIndexDispatchRecord current) =>
        expected.TaskId == current.TaskId && expected.VersionId == current.VersionId && expected.DocumentId == current.DocumentId
        && expected.DocumentVersion == current.DocumentVersion && expected.KnowledgeBaseVersion == current.KnowledgeBaseVersion
        && expected.LeaseToken == current.LeaseToken && expected.FileId == current.FileId && expected.ContentHash == current.ContentHash
        && expected.SizeBytes == current.SizeBytes && expected.ContentType == current.ContentType
        && expected.ModelConfigId == current.ModelConfigId && expected.ModelConfigVersion == current.ModelConfigVersion;
    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge embedding execution cannot hold a database transaction."); }
    private sealed record State(AiKnowledgeIndexDispatchRecord Source, AiKnowledgeBaseRecord Knowledge, AiModelConfigRecord Model);
}
