using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>只从已认证的耐久收件推进；跨作用域时创建全新DI/DbSession，绝不借用消费者事务。</summary>
internal sealed class WorkflowCollaborationRelay(IQueryExecutor query, IDataTransactionState state, IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> database, IOptions<WorkflowRecoveryWorkerOptions> options, IClock clock, WorkflowCollaborationScanCursor cursor,
    ILogger<WorkflowCollaborationRelay>? logger = null)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        if (state.HasTransaction) throw new InvalidOperationException("Collaboration Relay cannot run inside a consumer transaction.");
        var take = Math.Clamp(options.Value.BatchSize, 1, 8); var after = cursor.Read();
        var rows = await query.QueryAsync<WorkflowCollaborationRelayCandidate>(database.Value.Provider == DatabaseProvider.SqlServer
                ? WorkflowCollaborationRelaySql.PendingSqlServer : WorkflowCollaborationRelaySql.PendingMySql,
            WorkflowSqlParameters.Create(("Take", take), ("HasAfter", after.Id is null ? 0 : 1),
                ("AfterUpdatedAtUtc", after.UpdatedAtUtc ?? clock.UtcNow), ("AfterId", after.Id)), token).ConfigureAwait(false);
        cursor.Advance(rows, take);
        return await ProcessBatchAsync(rows, ProcessWithDeadlockRetryAsync, token,
            fenced => logger?.LogDebug("Collaboration candidate deferred by delivery fence for {EventType} schema {SchemaVersion}.", fenced.EventType, fenced.SchemaVersion)).ConfigureAwait(false);
    }

    /// <summary>单个流被回退保护时保留该候选供后续重试，同时继续处理其他候选。</summary>
    internal static async Task<int> ProcessBatchAsync(IReadOnlyList<WorkflowCollaborationRelayCandidate> rows,
        Func<WorkflowCollaborationRelayCandidate, CancellationToken, Task<bool>> process, CancellationToken token,
        Action<EventDeliveryProducerFencedException>? onFenced = null)
    {
        var completed = 0;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            try { if (await process(row, token).ConfigureAwait(false)) completed++; }
            catch (EventDeliveryProducerFencedException exception)
            {
                // 仅在候选的事务和作用域已退出后隔离此流；回退完成前保留耐久事实等待重试。
                token.ThrowIfCancellationRequested();
                onFenced?.Invoke(exception);
            }
        }
        return completed;
    }

    private async Task<bool> ProcessWithDeadlockRetryAsync(WorkflowCollaborationRelayCandidate candidate, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await ProcessAsync(candidate, token).ConfigureAwait(false); }
            catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.Deadlock)
            {
                // ProcessAsync的源/目标Scope均已退出，整个幂等单元重新读取；耗尽时保留事实等待下一轮，不能重放单条事务命令。
                if (attempt == 2) return false;
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), token).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> ProcessAsync(WorkflowCollaborationRelayCandidate candidate, CancellationToken token)
    {
        var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
            WorkflowSqlParameters.Create(("Id", candidate.Id), ("TenantScopeKey", candidate.SourceTenantScopeKey)), token).ConfigureAwait(false);
        if (admission is null || candidate.TargetTenantScopeKey != admission.TargetTenantScopeKey ||
            !WorkflowCollaborationAdmissionProof.TryRead(admission, out var binding)) return false;
        var proof = binding!;
        var readiness = await ReadSourceAsync(admission, proof, token).ConfigureAwait(false);
        if (readiness == SourceReadiness.Unknown) return false;
        await using (var target = scopes.CreateAsyncScope())
        {
            if (!await EnterAsync(target.ServiceProvider, proof.TargetTenantId, token).ConfigureAwait(false)) return false;
            var targetQuery = target.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var targetCommand = target.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var grant = await targetQuery.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
                WorkflowSqlParameters.Create(("Id", proof.GrantId), ("TenantScopeKey", proof.TargetTenantScopeKey)), token).ConfigureAwait(false);
            var authorized = grant is not null && grant.PolicyHash == proof.PolicyHash && WorkflowCollaborationGrantPolicy.TryAuthorize(grant,
                proof.TargetTenantScopeKey, true, proof.GrantRevision, clock.UtcNow, out _, out _);
            if (!authorized || readiness == SourceReadiness.Inactive)
                await targetCommand.ExecuteAsync(WorkflowCollaborationControlSql.CloseCall,
                    CloseParameters(admission, proof.TargetTenantScopeKey), token).ConfigureAwait(false);
            var current = await targetQuery.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
                CloseParameters(admission, proof.TargetTenantScopeKey), token).ConfigureAwait(false);
            if (current is null || current.BindingHash != admission.BindingHash) return false;
            if (current.StatusKey == "requested" && readiness == SourceReadiness.Active && authorized &&
                WorkflowCollaborationGrantPolicy.TryAuthorize(grant!, proof.TargetTenantScopeKey, true, proof.GrantRevision, clock.UtcNow, out _, out var accepted))
            {
                var members = accepted!.AllowedUserIds;
                var active = proof.TargetTenantId is null
                    ? (await target.ServiceProvider.GetRequiredService<IHostUserBatchSelectionDirectory>().FindActiveHostUsersAsync(members, token).ConfigureAwait(false)).Keys.ToHashSet()
                    : (await target.ServiceProvider.GetRequiredService<ITenantMemberSelectionDirectory>().FindActiveTenantMembersAsync(members, token).ConfigureAwait(false)).Keys.ToHashSet();
                if (members.Any(id => !active.Contains(id))) return false;
                using var projection = JsonDocument.Parse(current.ProjectionJson);
                var context = new WorkflowCollaborationStartContext(current, proof, accepted);
                var started = await target.ServiceProvider.GetRequiredService<WorkflowInstanceManagementService>().StartAsync(accepted.StarterUserId,
                    new(proof.TargetDefinitionVersionId, WorkflowCollaborationStartContext.BusinessType, proof.CallId.ToString("D"),
                        projection.RootElement.Clone(), context.IdempotencyKey), token, collaborationContext: context).ConfigureAwait(false);
                // 拒绝也需要可定位的机器码；不记录投影正文、成员或文件元数据，重复轮询仅使用Debug级别。
                if (!started.IsSuccess) logger?.LogDebug("Collaboration target start for {CallId} was refused: {ErrorCode} ({ErrorType}).",
                    proof.CallId, started.Error?.Code, started.Error?.Type);
                if (!started.IsSuccess && ClosesRejectedStart(started.Error!))
                    // 旧启动结果不能关闭另一执行者已经创建或推进的目标。
                    await targetCommand.ExecuteAsync(WorkflowCollaborationControlSql.CloseRejectedStart,
                        WorkflowSqlParameters.Create(("Id", current.Id), ("TenantScopeKey", proof.TargetTenantScopeKey),
                            ("BindingHash", current.BindingHash), ("Revision", current.Revision), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
                current = await targetQuery.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
                    CloseParameters(admission, proof.TargetTenantScopeKey), token).ConfigureAwait(false);
                if (current is null) return false;
            }
            var publisher = target.ServiceProvider.GetRequiredService<WorkflowCollaborationControlPublisher>();
            if (current.TargetInstanceId is not null) _ = await publisher.EnsureAsync(current, "created", token).ConfigureAwait(false);
            if (current.StatusKey == "cancel_requested")
            {
                if (current.TargetInstanceId is not null)
                    _ = await target.ServiceProvider.GetRequiredService<WorkflowInstanceManagementService>().CancelFromCollaborationAsync(current, token).ConfigureAwait(false);
                _ = await publisher.EnsureAsync(current, "cancelled", token).ConfigureAwait(false);
            }
            else if (current.StatusKey is "created" or "active") _ = await publisher.EnsureAsync(current, "completed", token).ConfigureAwait(false);
            admission = current;
        }
        // 目标事务与Scope已经退出，才允许进入源自己的接纳事务。
        await using var source = scopes.CreateAsyncScope();
        if (!await EnterAsync(source.ServiceProvider, proof.SourceTenantId, token).ConfigureAwait(false)) return false;
        return await source.ServiceProvider.GetRequiredService<WorkflowCollaborationSourceProcessor>().ProcessAsync(admission, token).ConfigureAwait(false);
    }

    private async Task<SourceReadiness> ReadSourceAsync(WorkflowCollaborationAdmissionRecord admission, WorkflowCollaborationCallBinding proof, CancellationToken token)
    {
        await using var source = scopes.CreateAsyncScope();
        if (!await EnterAsync(source.ServiceProvider, proof.SourceTenantId, token).ConfigureAwait(false)) return SourceReadiness.Inactive;
        var sourceQuery = source.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var delivery = await sourceQuery.QuerySingleOrDefaultAsync<WorkflowCollaborationDeliveryRecord>(WorkflowCollaborationCallSql.FindDelivery,
            WorkflowSqlParameters.Create(("DeliveryId", proof.DeliveryId), ("TenantScopeKey", proof.SourceTenantScopeKey)), token).ConfigureAwait(false);
        if (delivery is null || delivery.CallId != proof.CallId || delivery.BindingHash != admission.BindingHash || delivery.EnvelopeMessageId == Guid.Empty) return SourceReadiness.Unknown;
        var root = await sourceQuery.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", proof.SourceInstanceId), ("TenantScopeKey", proof.SourceTenantScopeKey)), token).ConfigureAwait(false);
        if (root is null || root.DefinitionVersionId != proof.SourceDefinitionVersionId || root.FormVersionId != proof.SourceFormVersionId) return SourceReadiness.Unknown;
        var activeStep = await sourceQuery.QuerySingleOrDefaultAsync<long?>(WorkflowCollaborationControlSql.ActiveSourceStep,
            WorkflowSqlParameters.Create(("Id", proof.CallId), ("InstanceId", root.Id), ("TenantScopeKey", proof.SourceTenantScopeKey), ("BindingHash", admission.BindingHash)), token).ConfigureAwait(false);
        var targetActive = proof.TargetTenantId is not { } targetId || await source.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>()
            .ResolveActiveByIdAsync(targetId, token).ConfigureAwait(false) is not null;
        if (root.StatusKey is "completed" or "rejected" or "cancelled" || activeStep is null || !targetActive ||
            !await WorkflowExecutionFrameScope.IsRetainedAsync(sourceQuery, root, proof.ExecutionEpochId, proof.ParallelJoinId, proof.ParallelBranchKey, token).ConfigureAwait(false))
        {
            await source.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(WorkflowCollaborationControlSql.CloseCall,
                CloseParameters(admission, proof.SourceTenantScopeKey), token).ConfigureAwait(false);
            return SourceReadiness.Closed;
        }
        return root.StatusKey == "active" ? SourceReadiness.Active : SourceReadiness.Paused;
    }

    private object CloseParameters(WorkflowCollaborationAdmissionRecord admission, string scope) => WorkflowSqlParameters.Create(
        ("Id", admission.Id), ("TenantScopeKey", scope), ("BindingHash", admission.BindingHash), ("Now", clock.UtcNow));
    // 固定子流程无法在获准切片内激活是明确拒绝；修订竞争及暂态冲突仍保留原调用重试。
    internal static bool ClosesRejectedStart(Error error) => error.Type == ErrorType.Validation || error.Code == WorkflowErrorCodes.SubprocessActivationInvalid;
    private enum SourceReadiness { Unknown, Active, Paused, Closed, Inactive }

    private static async Task<bool> EnterAsync(IServiceProvider services, Guid? tenantId, CancellationToken token)
    {
        if (services.GetRequiredService<IDataTransactionState>().HasTransaction) throw new InvalidOperationException("A fresh Relay scope already has a transaction.");
        var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
        if (tenantId is null) { writer.SetHost(); return true; }
        var tenant = await services.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(tenantId.Value, token).ConfigureAwait(false);
        if (tenant is null) return false;
        writer.SetTenant(tenant); return true;
    }
}
