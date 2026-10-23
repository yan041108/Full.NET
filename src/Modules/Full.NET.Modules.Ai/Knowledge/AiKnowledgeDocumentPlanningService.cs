using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整本地多格式文档规划；不能缓存为外发授权，也不改变任务状态。</summary>
internal sealed record AiKnowledgeIndexDocumentPreparation(AiKnowledgeIndexDispatchRecord Source,
    SessionBindingSnapshot Session, AiKnowledgeDocumentPlan Plan, AiOperationRequest ModelBinding);

/// <summary>连接原会话来源读取与隔离解析；后续付费调用仍须另行复核。</summary>
internal sealed class AiKnowledgeDocumentPlanningService(IQueryExecutor query, ICurrentTenant tenant,
    IDataTransactionState transactions, AiKnowledgeIndexTaskStore tasks, AiKnowledgeIndexSessionService sessions,
    IBackgroundSessionAuthorization authorization, IHostFileReferenceReader references, IHostFileContentReader hostFiles,
    ITenantResourceFileStore tenantFiles, AiKnowledgeDocxSandboxOptions sandbox, int timeoutMs = 25000,
    AiKnowledgeDocumentSandboxOptions? documentSandbox = null, AiKnowledgeDocumentParseGate? parseGate = null)
{
    private bool UsesDocumentParser => documentSandbox is { Enabled: true };
    internal int ExecutionBudgetMilliseconds => UsesDocumentParser ? documentSandbox!.TimeoutMs + 25000 : 25000;
    internal int PhaseSeconds => UsesDocumentParser ? 150 : 25;
    private static readonly AiKnowledgeChunkOptions ChunkOptions = new();
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(5);
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>只接受有效租约的原来源；任何阶段失效都不返回部分逻辑块或部分规划。</summary>
    /// <remarks>工作预算覆盖整个过程，清理使用独立上限；不续租、不改批准、不调用模型或写任务。</remarks>
    internal async Task<AiKnowledgeIndexDocumentPreparation?> PlanAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, CancellationToken token)
    {
        Check(token); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        if (timeoutMs is < 1 or > 25000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(UsesDocumentParser ? documentSandbox!.TimeoutMs : timeoutMs); var bounded = deadline.Token;
        var session = await BoundAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), bounded).ConfigureAwait(false);
        if (session is null) return null;
        var state = await CurrentAsync(binding, taskId, leaseToken, session, bounded).ConfigureAwait(false);
        if (state is null || !(UsesDocumentParser ? AiKnowledgeDocumentFormats.IsDocument(state.Source.ContentType) : state.Source.ContentType == AiKnowledgeFilePolicy.DocxContentType)
            || AiKnowledgeDocumentFormats.Policy(state.Source.ContentType, state.Source.ParsingPolicyKey) is null
            || state.Source.SizeBytes <= 0 || state.Source.SizeBytes > AiKnowledgeFilePolicy.Limit(state.Source.ContentType)) return null;
        if (UsesDocumentParser && parseGate is null) throw new InvalidOperationException("Document parsing admission is required.");
        using var slot = UsesDocumentParser ? await parseGate!.EnterAsync(bounded).ConfigureAwait(false) : null;
        if (!await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return null;
        Stream? owned = null;
        try
        {
            var opened = await OpenAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false);
            if (opened is null) return null;
            owned = opened.Content;
            Check(bounded);
            if (opened.ContentType != state.Source.ContentType
                || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return null;
            // 从此读取器接管流释放责任；解析器只取得独占缓存，不持有 Files 流或存储路径。
            var transferred = owned; owned = null;
            // 复用格式无关的独占字节读取与释放边界；本入口先应用多格式文档的精确大小上限。
            using var bytes = await new AiKnowledgePdfSourceReader(transactions, timeoutMs, checked((int)AiKnowledgeFilePolicy.Limit(state.Source.ContentType)))
                .ReadAsync(transferred, state.Source.ContentHash, state.Source.SizeBytes, bounded).ConfigureAwait(false);
            if (bytes is null || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return null;
            AiKnowledgeDocumentModelChunks? chunks;
            if (UsesDocumentParser)
            {
                var parsed = await new AiKnowledgeDocumentSandboxParser(transactions, documentSandbox!)
                    .ParseAsync(bytes.Bytes, state.Source.ContentHash, checked((int)state.Source.SizeBytes), state.Source.ContentType, state.Source.ParsingPolicyKey, bounded).ConfigureAwait(false);
                if (parsed is null) return null;
                var result = AiKnowledgeTelemetry.Run("knowledge_chunk_document", () => AiKnowledgeDocumentModelChunker.Chunk(state.Knowledge, state.Model, parsed, ChunkOptions, bounded), bounded);
                if (result.ChunkFailure == AiKnowledgeChunkFailure.UncertainSource) throw new AiKnowledgeDocumentParsingException(AiKnowledgeIndexFailure.OcrLowConfidence);
                chunks = result.Output;
            }
            else
            {
                var parsed = await new AiKnowledgeDocxSandboxParser(transactions, sandbox)
                    .ParseAsync(bytes.Bytes, state.Source.ContentHash, checked((int)state.Source.SizeBytes), bounded).ConfigureAwait(false);
                if (parsed is null) return null;
                chunks = AiKnowledgeDocxModelChunker.Chunk(state.Knowledge, state.Model, parsed, ChunkOptions, bounded).Output;
            }
            if (chunks is null || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return null;
            Check(bounded);
            var plan = AiKnowledgeDocumentPlan.Create(chunks with { ParserRuntimeId = UsesDocumentParser ? documentSandbox!.ImageId : sandbox.ImageId }, bounded);
            if (plan is null || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return null;
            Check(bounded);
            // 指纹绑定实际端点和组织选项，不携带凭据；此快照仍不能替代后续派发前权威复核。
            return new(state.Source, session, plan, Fingerprint(taskId, state.Model));
        }
        finally
        {
            // 接管前的 MIME/撤权/取消拒绝也必须释放 Files 交付的流，且不能读取正文。
            if (owned is not null) await CloseAsync(owned).ConfigureAwait(false);
        }
    }

    /// <summary>后续等待只复核原计划权威，不再次打开正文；返回模型仅供受控客户端绑定。</summary>
    internal async Task<AiModelConfigRecord?> ReadCurrentModelAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, AiKnowledgeIndexDocumentPreparation prepared, CancellationToken token)
    {
        Check(token); ArgumentNullException.ThrowIfNull(prepared);
        _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var current = await CurrentAsync(binding, taskId, leaseToken, prepared.Session, deadline.Token).ConfigureAwait(false);
        Check(deadline.Token);
        if (current is null || !AiKnowledgeIndexEmbeddingService.SameSource(prepared.Source, current.Source)
            || prepared.ModelBinding != Fingerprint(taskId, current.Model) || !MatchesPlan(current, binding, prepared.Plan)
            || !await FileReadyAsync(current.Source, binding, deadline.Token).ConfigureAwait(false)) return null;
        var final = await CurrentAsync(binding, taskId, leaseToken, prepared.Session, deadline.Token).ConfigureAwait(false);
        return final is not null && SameState(current, final, taskId) && MatchesPlan(final, binding, prepared.Plan) ? final.Model : null;
    }

    /// <summary>恢复不可变完整清单并复核当前来源及目的地；不重新解析、不重新收费。</summary>
    internal async Task<AiKnowledgeIndexDocumentPreparation?> RestoreAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, AiKnowledgeDocumentPlan plan, CancellationToken token)
    {
        Check(token); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var session = await BoundAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), deadline.Token).ConfigureAwait(false);
        if (session is null) return null;
        var state = await CurrentAsync(binding, taskId, leaseToken, session, deadline.Token).ConfigureAwait(false);
        if (state is null || !MatchesPlan(state, binding, plan) || !await FileReadyAsync(state.Source, binding, deadline.Token).ConfigureAwait(false)) return null;
        var final = await CurrentAsync(binding, taskId, leaseToken, session, deadline.Token).ConfigureAwait(false);
        if (final is null || !SameState(state, final, taskId) || !MatchesPlan(final, binding, plan)) return null;
        return new(final.Source, session, plan, Fingerprint(taskId, final.Model));
    }

    private bool MatchesPlan(State state, AiKnowledgeIndexTaskBinding binding, AiKnowledgeDocumentPlan plan)
    {
        var s = plan.Source;
        return AiKnowledgeModelChunker.ValidateApproval(state.Knowledge, state.Model) == AiKnowledgeModelChunkFailure.None
            && s.KnowledgeBaseId == binding.KnowledgeBaseId && s.KnowledgeBaseVersion == binding.KnowledgeBaseVersion
            && s.TenantId == tenant.Id && s.ModelConfigId == state.Model.Id && s.ModelConfigVersion == state.Model.Version
            && s.ProviderKey == state.Model.ProviderKey && s.ModelId == state.Model.ModelId
            && s.DataClassification == state.Source.DataClassification && s.DestinationHash == AiKnowledgeDocumentModelChunker.Destination(state.Model)
            && s.Document.SizeBytes == state.Source.SizeBytes && string.Equals(s.Document.ContentHash, state.Source.ContentHash, StringComparison.OrdinalIgnoreCase)
            && AiKnowledgeDocumentFormats.Matches(s.Document, state.Source.ContentType, state.Source.ParsingPolicyKey);
    }

    // Files 的不可变引用继续按当前可读状态复核；租户使用原版本资源，不接受另一个资源的同名文件。
    private async Task<bool> FileReadyAsync(AiKnowledgeIndexDispatchRecord source, AiKnowledgeIndexTaskBinding binding, CancellationToken token)
    {
        Check(token);
        if (!tasks.IsTenant)
        {
            var file = await BoundAsync(ct => references.GetReadyReferenceAsync(source.FileId, ct), token).ConfigureAwait(false);
            return file is not null && file.FileId == source.FileId && file.SizeBytes == source.SizeBytes
                && string.Equals(file.ContentHash, source.ContentHash, StringComparison.OrdinalIgnoreCase);
        }
        var files = await BoundAsync(ct => tenantFiles.ListReadyAsync("ai", binding.VersionId, ct), token).ConfigureAwait(false);
        var count = files?.Count ?? 0; if (count is < 1 or > 25) return false;
        var matches = 0;
        for (var index = 0; index < count; index++) { Check(token); if (files![index].FileId == source.FileId) matches++; }
        return matches == 1;
    }

    private async Task<State?> CurrentAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        SessionBindingSnapshot session, CancellationToken token)
    {
        Check(token);
        if (await BoundAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false) != session
            || !await AuthorizedAsync(binding, session, token).ConfigureAwait(false)) return null;
        var source = await BoundAsync(ct => tasks.ReadDispatchAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        if (source is null) return null;
        var state = await ReadStateAsync(binding, source, token).ConfigureAwait(false);
        if (state is null) return null;
        if (await BoundAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false) != session
            || !await AuthorizedAsync(binding, session, token).ConfigureAwait(false)) return null;
        // Identity 等待后再读取 AI 权威，捕获同版本目的地/分类漂移；不声称跨模块原子快照。
        var finalState = await ReadStateAsync(binding, source, token).ConfigureAwait(false);
        if (finalState is null || !SameState(state, finalState, taskId)
            || await BoundAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false) != session) return null;
        var final = await BoundAsync(ct => tasks.ReadDispatchAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        Check(token);
        return final is not null && AiKnowledgeIndexEmbeddingService.SameSource(source, final)
            && MatchesProjection(finalState, final)
            ? finalState with { Source = final } : null;
    }

    private async Task<State?> ReadStateAsync(AiKnowledgeIndexTaskBinding binding, AiKnowledgeIndexDispatchRecord source, CancellationToken token)
    {
        Check(token);
        var knowledge = await BoundAsync(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(tasks.IsTenant ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost,
            AiSqlParameters.Create(("Id", binding.KnowledgeBaseId), ("OwnerUserId", binding.OwnerUserId)), ct), token).ConfigureAwait(false);
        Check(token);
        var model = await BoundAsync(ct => query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", source.ModelConfigId)), ct), token).ConfigureAwait(false);
        Check(token);
        return knowledge is not null && model is not null && knowledge.Id == binding.KnowledgeBaseId
            && knowledge.OwnerUserId == binding.OwnerUserId && knowledge.TenantId == tenant.Id
            && knowledge.Version == binding.KnowledgeBaseVersion && knowledge.DataClassification is "public" or "internal" or "restricted"
            && model.IsEnabled && model.Id == source.ModelConfigId && model.Version == source.ModelConfigVersion
            && model.TenantId != Guid.Empty && (!model.TenantId.HasValue || model.TenantId == tenant.Id)
            && AiKnowledgePolicy.Allows(knowledge.IsEnabled, knowledge.EmbeddingModelConfigId, knowledge.EmbeddingModelVersion, model.Id, model.Version)
            ? new(source, knowledge, model) : null;
    }

    private async Task<bool> AuthorizedAsync(AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session, CancellationToken token)
    {
        Check(token);
        if (session.UserId != binding.OwnerUserId || session.SessionId == Guid.Empty || session.TenantId != tenant.Id) return false;
        {
            var actor = await BoundAsync(ct => authorization.AuthorizeAllAsync(session, Permissions, ct), token).ConfigureAwait(false);
            Check(token);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return false;
        }
        return true;
    }

    private async Task<bool> StillCurrentAsync(State expected, AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, SessionBindingSnapshot session, CancellationToken token)
    {
        var current = await CurrentAsync(binding, taskId, leaseToken, session, token).ConfigureAwait(false);
        return current is not null && SameState(expected, current, taskId);
    }

    private static bool SameState(State expected, State current, Guid taskId) =>
        AiKnowledgeIndexEmbeddingService.SameSource(expected.Source, current.Source)
        && expected.Knowledge.DataClassification == current.Knowledge.DataClassification
        && Fingerprint(taskId, expected.Model) == Fingerprint(taskId, current.Model);

    private static AiOperationRequest Fingerprint(Guid taskId, AiModelConfigRecord model) =>
        AiEmbeddingBudgetRequest.Create(taskId, model, ["binding-check"]);

    private static bool MatchesProjection(State expected, AiKnowledgeIndexDispatchRecord row) =>
        row.DataClassification == expected.Knowledge.DataClassification && row.ModelProviderKey == expected.Model.ProviderKey
        && row.ModelId == expected.Model.ModelId && row.ModelEndpointBaseUrl == expected.Model.EndpointBaseUrl
        && row.ModelOrganizationId == expected.Model.OrganizationId;

    private async Task<Opened?> OpenAsync(State state, AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, SessionBindingSnapshot session, CancellationToken token)
    {
        Check(token);
        if (!tasks.IsTenant)
        {
            var reference = await BoundAsync(ct => references.GetReadyReferenceAsync(state.Source.FileId, ct), token).ConfigureAwait(false);
            Check(token);
            if (reference is null || reference.FileId != state.Source.FileId || reference.SizeBytes != state.Source.SizeBytes
                || !string.Equals(reference.ContentHash, state.Source.ContentHash, StringComparison.OrdinalIgnoreCase)
                || !await StillCurrentAsync(state, binding, taskId, leaseToken, session, token).ConfigureAwait(false)) return null;
        }
        Check(token); var scoped = tasks.IsTenant;
        // 失败清理回调接管迟到流；工作令牌不会交给 Files 注册不合作的取消回调。
        return await BoundAsync<Opened?>(async ct =>
        {
            if (scoped)
            {
                var result = await tenantFiles.OpenReadyContentAsync("ai", binding.VersionId, state.Source.FileId, ct).ConfigureAwait(false);
                return result.IsSuccess ? new Opened(result.Value!.Content, result.Value.ContentType) : null;
            }
            var host = await hostFiles.OpenReadyContentAsync(state.Source.FileId, ct).ConfigureAwait(false);
            return host.IsSuccess ? new Opened(host.Value!.Content, host.Value.ContentType) : null;
        }, token, result => result is null ? Task.CompletedTask : CloseAsync(result.Content)).ConfigureAwait(false);
    }

    private async Task<T> BoundAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token, Func<T, Task>? closeLate = null)
    {
        Check(token); var io = new CancellationTokenSource(); var handedOff = false;
        // 独立 IO 令牌不能链接工作令牌；回调只进入异步清理，不能阻塞调用者 Cancel。
        var work = AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async _ => { Check(io.Token); return await action(io.Token).ConfigureAwait(false); }, CancellationToken.None);
        try
        {
            var result = await work.WaitAsync(token).ConfigureAwait(false);
            Check(token); handedOff = true; return result;
        }
        finally
        {
            if (handedOff) io.Dispose();
            else
            {
                // 观察取消回调异常也不能跳过晚到结果的释放；超时只证明已拒绝，不能声称操作已停止。
                var cleanup = Task.WhenAll(ObserveAsync(io.CancelAsync()), CompleteLateAsync(work, closeLate));
                try { await cleanup.WaitAsync(CleanupLimit).ConfigureAwait(false); }
                catch (TimeoutException) { throw new IOException("多格式文档boundary cleanup was not confirmed."); }
                finally
                {
                    if (cleanup.IsCompleted) io.Dispose();
                    else _ = DisposeAfterAsync(cleanup, io);
                }
            }
        }
    }

    private static async Task CompleteLateAsync<T>(Task<T> work, Func<T, Task>? close)
    {
        T result;
        try { result = await work.ConfigureAwait(false); } catch { return; }
        if (close is not null) await close(result).ConfigureAwait(false);
    }

    private static async Task DisposeAfterAsync(Task cleanup, CancellationTokenSource io)
    { await ObserveAsync(cleanup).ConfigureAwait(false); io.Dispose(); }

    private static async Task CloseAsync(Stream stream)
    {
        var closing = AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async _ => { await stream.DisposeAsync().ConfigureAwait(false); return true; }, CancellationToken.None);
        try { await closing.WaitAsync(CleanupLimit).ConfigureAwait(false); }
        catch (TimeoutException) { _ = ObserveAsync(closing); throw new IOException("多格式文档source cleanup was not confirmed."); }
        catch { throw new IOException("多格式文档source disposal failed."); }
    }

    private static async Task ObserveAsync(Task work)
    { try { await work.ConfigureAwait(false); } catch { } }

    private void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("多格式文档planning cannot hold a database transaction.");
    }

    private sealed record State(AiKnowledgeIndexDispatchRecord Source, AiKnowledgeBaseRecord Knowledge, AiModelConfigRecord Model);
    private sealed record Opened(Stream Content, string ContentType);
}
