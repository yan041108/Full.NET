using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>独立游标区分准备和补偿，暂停或失效的早期调用不能占满后续窗口。</summary>
internal sealed class WorkflowCollaborationFileCursors
{
    internal WorkflowCollaborationScanCursor Preparing { get; } = new();
    internal WorkflowCollaborationScanCursor Cleanup { get; } = new();
}

/// <summary>源调用先提交，文件准备不借用业务事务，冻结后才允许发布请求。</summary>
internal sealed partial class WorkflowCollaborationFilePreparationProcessor(IQueryExecutor query, IDataTransactionState state,
    IServiceScopeFactory scopes, IOptions<DatabaseOptions> database, IOptions<WorkflowRecoveryWorkerOptions> options,
    IClock clock, WorkflowCollaborationFileCursors cursors)
{
    internal async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        if (state.HasTransaction) throw new InvalidOperationException("File preparation cannot borrow a consumer transaction.");
        var take = Math.Clamp(options.Value.BatchSize, 1, 8); var saved = 0;
        foreach (var cleanup in new[] { false, true })
        {
            var cursor = cleanup ? cursors.Cleanup : cursors.Preparing; var after = cursor.Read();
            var sql = (database.Value.Provider == DatabaseProvider.SqlServer, cleanup) switch
            {
                (true, false) => WorkflowCollaborationFileSql.PendingSqlServer, (false, false) => WorkflowCollaborationFileSql.PendingMySql,
                (true, true) => WorkflowCollaborationFileSql.CleanupSqlServer, _ => WorkflowCollaborationFileSql.CleanupMySql
            };
            var rows = await query.QueryAsync<WorkflowCollaborationRelayCandidate>(sql, P(("Take", take), ("Now", clock.UtcNow),
                ("HasAfter", after.Id is null ? 0 : 1), ("AfterUpdatedAtUtc", after.UpdatedAtUtc ?? clock.UtcNow), ("AfterId", after.Id)), token).ConfigureAwait(false);
            cursor.Advance(rows, take);
            foreach (var row in rows)
            {
                await using var source = scopes.CreateAsyncScope();
                if (!EnterKnown(source.ServiceProvider, row.SourceTenantScopeKey)) continue;
                var current = await ReadAsync(source.ServiceProvider, row.Id, row.SourceTenantScopeKey, token).ConfigureAwait(false);
                if (current is null || current.Binding.TargetTenantScopeKey != row.TargetTenantScopeKey) continue;
                if (cleanup) { await ReleaseBothAsync(current.Binding, token).ConfigureAwait(false); continue; }
                var tenants = source.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>();
                var enabled = (current.Binding.SourceTenantId is not { } issuer || await tenants.ResolveActiveByIdAsync(issuer, token).ConfigureAwait(false) is not null) &&
                    (current.Binding.TargetTenantId is not { } recipient || await tenants.ResolveActiveByIdAsync(recipient, token).ConfigureAwait(false) is not null);
                var live = WorkflowCollaborationGrantPolicy.TryAuthorize(current.Grant, current.Call.TenantScopeKey, false, current.Binding.GrantRevision,
                    clock.UtcNow, out _, out _) && current.Grant.PolicyHash == current.Binding.PolicyHash;
                var ownQuery = source.ServiceProvider.GetRequiredService<IQueryExecutor>();
                var retained = await IsRetainedAsync(ownQuery, current, token).ConfigureAwait(false);
                if (!enabled || !live || !retained)
                {
                    if (await CloseAsync(source.ServiceProvider, current, token).ConfigureAwait(false)) saved++;
                    continue;
                }
                if (current.Root.StatusKey != "active") continue;
                var projection = await PrepareAsync(source.ServiceProvider, current, token).ConfigureAwait(false);
                // 固定输入或本地持有校验失败不是可重试投递；关闭并清理，不能把外来能力再委托或无限占用预算。
                if (projection is null)
                {
                    if (await CloseAsync(source.ServiceProvider, current, token).ConfigureAwait(false)) saved++;
                }
                else if (await FreezeAsync(source.ServiceProvider, current, projection, token).ConfigureAwait(false)) saved++;
            }
        }
        return saved;
    }

    private static bool EnterKnown(IServiceProvider services, string scope)
    {
        var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
        if (scope == "host") { writer.SetHost(); return true; }
        if (scope.Length != 39 || !scope.StartsWith("tenant:", StringComparison.Ordinal) || !Guid.TryParseExact(scope[7..], "N", out var id) || id == Guid.Empty) return false;
        // 持久作用域只用于精确调用的关闭/Files补偿；正常准备另行复核双方活动目录。
        writer.SetTenant(new TenantContext(id, "workflow-file-recovery", "Workflow文件恢复")); return true;
    }

    private static async Task<PreparationState?> ReadAsync(IServiceProvider services, Guid id, string scope, CancellationToken token)
    {
        var query = services.GetRequiredService<IQueryExecutor>();
        var call = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, P(("Id", id), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (call is null || call.TenantScopeKey != scope || WorkflowCollaborationCallPolicy.Hash(call.InputJson) != call.InputHash) return null;
        WorkflowCollaborationCallBinding? binding;
        try { binding = JsonSerializer.Deserialize(call.BindingJson, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding); }
        catch (JsonException) { return null; }
        if (binding is null) return null;
        var proof = new WorkflowCollaborationAdmissionRecord(call.Id, scope, binding.TargetTenantScopeKey, call.DeliveryId, call.GrantId,
            call.BindingJson, call.BindingHash, call.ProjectionJson, call.ProjectionHash, "requested", null, 1, call.CreatedAtUtc, call.UpdatedAtUtc);
        if (!WorkflowCollaborationAdmissionProof.TryRead(proof, out var verified) || verified != binding || binding.SourceInstanceId != call.InstanceId ||
            binding.InputHash != call.InputHash || binding.InputFormRevision != call.InputFormRevision || binding.ExecutionEpochId != call.ExecutionEpochId ||
            binding.ReservedActivities != call.ReservedActivities) return null;
        var root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, P(("Id", call.InstanceId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find, P(("Id", call.GrantId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (root is null || root.TenantId != binding.SourceTenantId || root.DefinitionVersionId != binding.SourceDefinitionVersionId || root.FormVersionId != binding.SourceFormVersionId ||
            grant is null || grant.SourceTenantScopeKey != scope || grant.TargetTenantScopeKey != binding.TargetTenantScopeKey || grant.PolicyHash != binding.PolicyHash ||
            !WorkflowCollaborationGrantPolicy.TryReadAgreement(grant, out var offer, out var accepted)) return null;
        return new(call, binding, root, grant, offer!, accepted!);
    }

    private static async Task<bool> IsRetainedAsync(IQueryExecutor query, PreparationState value, CancellationToken token) =>
        await query.QuerySingleOrDefaultAsync<long?>(WorkflowCollaborationControlSql.ActiveSourceStep,
            P(("Id", value.Call.Id), ("InstanceId", value.Root.Id), ("TenantScopeKey", value.Call.TenantScopeKey), ("BindingHash", value.Call.BindingHash)), token).ConfigureAwait(false) is not null &&
        await WorkflowExecutionFrameScope.IsRetainedAsync(query, value.Root, value.Binding.ExecutionEpochId, value.Binding.ParallelJoinId, value.Binding.ParallelBranchKey, token).ConfigureAwait(false);

    private async Task ReleaseBothAsync(WorkflowCollaborationCallBinding binding, CancellationToken token)
    {
        foreach (var key in new[] { binding.SourceTenantScopeKey, binding.TargetTenantScopeKey })
        {
            await using var cleanup = scopes.CreateAsyncScope();
            if (EnterKnown(cleanup.ServiceProvider, key))
                _ = await cleanup.ServiceProvider.GetRequiredService<IWorkflowFileReadCapabilityStore>().ReleaseIssuedAsync(binding.CallId, token).ConfigureAwait(false);
        }
    }
    private static Dictionary<string, object?> P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private sealed record PreparationState(WorkflowCollaborationCallRecord Call, WorkflowCollaborationCallBinding Binding, WorkflowInstanceRecord Root,
        WorkflowCollaborationGrantFact Grant, WorkflowCollaborationOfferPolicy Offer, WorkflowCollaborationAcceptancePolicy Accepted);
}
