using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Execution.BusinessCommands;

/// <summary>回执只先接纳为本地持久事实；后继由独立工作推进，暂停或取消不会被迟到结果绕过。</summary>
internal sealed class WorkflowBusinessCommandResultService(IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, ICurrentTenant tenant, IClock clock, IIdGenerator ids, WorkflowBusinessActionCatalog catalog)
{
    public async Task AcceptAsync(Guid? messageTenantId, WorkflowBusinessCommandResultIntegrationEvent value, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (scope.TenantId != messageTenantId || !WorkflowBusinessCommandValidation.ValidResult(value))
            throw new InvalidOperationException("workflow.business_command.result_invalid");
        var accepted = await transaction.ExecuteResultAsync(inner => AcceptCoreAsync(scope, value, inner), token).ConfigureAwait(false);
        if (!accepted.IsSuccess) throw new InvalidOperationException("workflow.business_command.result_conflict");
    }
    private async Task<Result<bool>> AcceptCoreAsync(WorkflowManagementScope scope, WorkflowBusinessCommandResultIntegrationEvent value, CancellationToken token)
    {
        // 取消后的补偿回执仍必须接纳；先锁根再读取状态，不能复用仅允许活动根的人审锁。
        if (await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                WorkflowSqlParameters.Create(("Id", value.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
            return Result<bool>.Success(false);
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", value.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance is null) return Result<bool>.Success(false);
        var row = await query.QuerySingleOrDefaultAsync<WorkflowBusinessCommandRecord>(WorkflowBusinessCommandSql.Find,
            WorkflowSqlParameters.Create(("Id", value.CallId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (row is null) return Result<bool>.Success(false);
        if (WorkflowBusinessCommandValidation.Read(row, instance, catalog) is null || row.InputHash != value.InputHash || value.Generation > row.Generation) return Invalid();
        if (value.Generation < row.Generation) return Result<bool>.Success(false);
        if (value.PhaseKey == "compensate" && !row.CompensationRequested || value.PhaseKey == "execute" && row.CompensationRequested) return Invalid();
        if (row.OwnerInputHash is { } knownHash && value.OwnerInputHash is { } suppliedHash && knownHash != suppliedHash ||
            row.AppliedVersion is { } knownVersion && value.AppliedVersion is { } suppliedVersion && knownVersion != suppliedVersion) return Invalid();
        var resultJson = JsonSerializer.Serialize(value, WorkflowJsonSerializerContext.Default.WorkflowBusinessCommandResultIntegrationEvent);
        if (row.ResultJson is not null)
        {
            // 发生时间不作为效果身份；相同效果的新MessageId仍然是无写入重放。
            WorkflowBusinessCommandResultIntegrationEvent? previous;
            try { previous = JsonSerializer.Deserialize(row.ResultJson, WorkflowJsonSerializerContext.Default.WorkflowBusinessCommandResultIntegrationEvent); }
            catch (JsonException) { return Invalid(); }
            return previous is not null && (previous with { OccurredAtUtc = value.OccurredAtUtc }) == value ? Result<bool>.Success(false) : Invalid();
        }
        var failedRoot = instance.StatusKey is "rejected" or "cancelled";
        var status = value.OutcomeKey switch {
            "applied" when !failedRoot => "result_ready",
            "applied" => "applied",
            "cancelled" => "cancelled",
            "compensated" => "compensated",
            "declined" => "declined",
            _ => "faulted" };
        if (instance.StatusKey == "completed") return Invalid();
        if (!failedRoot && value.OutcomeKey is "cancelled" or "compensated") status = "faulted";
        if (await commands.ExecuteAsync(WorkflowBusinessCommandSql.AcceptResult, WorkflowSqlParameters.Create(("Id", row.Id), ("InstanceId", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("InputHash", row.InputHash), ("Revision", row.Revision), ("Generation", row.Generation),
                ("StatusKey", status), ("OwnerInputHash", value.OwnerInputHash ?? row.OwnerInputHash), ("AppliedVersion", value.AppliedVersion ?? row.AppliedVersion),
                ("CompensatedVersion", value.CompensatedVersion ?? row.CompensatedVersion), ("ErrorCode", value.ErrorCode), ("ResultJson", resultJson), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) return Invalid();
        if (status is "faulted" or "declined" && instance.StatusKey == "active" && await commands.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1) return Invalid();
        var detail = $"{{\"sourceKey\":\"business_command\",\"phaseKey\":\"{value.PhaseKey}\",\"outcomeKey\":\"{value.OutcomeKey}\",\"generation\":{value.Generation}}}";
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", instance.TenantId),
            ("ScopeKey", instance.ScopeKey), ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null),
            ("ResourceTypeKey", "step"), ("ResourceId", row.Id), ("OutcomeKey", status is "faulted" or "declined" ? "failed" : "succeeded"), ("DetailJson", detail),
            ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
