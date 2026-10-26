using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

/// <summary>发起人催办只追加有界通知事实，既不改变办理修订，也不消耗自动 SLA 预算。</summary>
internal sealed class WorkflowManualReminderService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, IHostUserBatchSelectionDirectory hosts,
    ITenantMemberSelectionDirectory members, WorkflowNotificationOutboxPublisher publisher)
{
    public async Task<Result<WorkflowManualReminderTargetsResponse>> ListAsync(Guid instanceId, Guid actor, CancellationToken token = default)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        var instance = await InstanceAsync(instanceId, scope, token).ConfigureAwait(false);
        if (instance is null) return Fail<WorkflowManualReminderTargetsResponse>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        if (instance.StartedById != actor) return Fail<WorkflowManualReminderTargetsResponse>(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        var parameters = P(instanceId, actor, scope);
        var targets = await query.QueryAsync<WorkflowManualReminderTargetRecord>(WorkflowManualReminderSql.ListTargets, parameters, token).ConfigureAwait(false);
        var asset = await AssetAsync(instance, scope, token).ConfigureAwait(false);
        var results = new List<WorkflowManualReminderTargetResponse>();
        foreach (var group in targets.GroupBy(x => x.StepId))
        {
            var first = group.First();
            if (group.Count() > WorkflowManualReminderRules.MaximumRecipients || !Allows(asset, first)) continue;
            parameters["StepId"] = first.StepId;
            var budget = await query.QuerySingleOrDefaultAsync<WorkflowManualReminderBudgetRecord>(WorkflowManualReminderSql.ReadBudget, parameters, token).ConfigureAwait(false);
            results.Add(new(first.StepId, first.NodeKey, first.NodeTypeKey, first.StepRevision, group.Count(), budget?.ManualCount ?? 0,
                budget?.LastRequestedAtUtc, WorkflowManualReminderRules.CanRequest(budget?.ManualCount ?? 0, budget?.LastRequestedAtUtc, clock.UtcNow)));
        }
        return Result<WorkflowManualReminderTargetsResponse>.Success(new(instance.Revision, results));
    }

    public async Task<Result<WorkflowManualReminderResponse>> RequestAsync(Guid instanceId, Guid actor, RequestWorkflowManualReminder request,
        CancellationToken token = default)
    {
        if (actor == Guid.Empty || request.StepId == Guid.Empty || request.ExpectedInstanceRevision < 1 || request.ExpectedStepRevision < 1 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            return Fail<WorkflowManualReminderResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        request = request with { IdempotencyKey = request.IdempotencyKey.Trim() };
        var scope = WorkflowManagementScope.Resolve(tenant);
        var instance = await InstanceAsync(instanceId, scope, token).ConfigureAwait(false);
        if (instance is null) return Fail<WorkflowManualReminderResponse>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        if (instance.StartedById != actor) return Fail<WorkflowManualReminderResponse>(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        var parameters = P(instanceId, actor, scope);
        parameters["IdempotencyKey"] = request.IdempotencyKey;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,
            WorkflowJsonSerializerContext.Default.RequestWorkflowManualReminder))));
        var receipt = await query.QuerySingleOrDefaultAsync<WorkflowManualReminderReceiptRecord>(WorkflowManualReminderSql.FindReceipt, parameters, token).ConfigureAwait(false);
        if (receipt is not null) return Replay(receipt, actor, hash);
        // 跨模块活动身份在本地事务之外准备，锁内只消费并重新核对实际待办元组。
        var prepared = (await query.QueryAsync<WorkflowManualReminderTargetRecord>(WorkflowManualReminderSql.ListTargets, parameters, token).ConfigureAwait(false))
            .Where(x => x.StepId == request.StepId).ToArray();
        if (prepared.Length is < 1 or > WorkflowManualReminderRules.MaximumRecipients) return Conflict();
        var recipients = prepared.Select(x => x.AssigneeUserId).Distinct().ToArray();
        var valid = scope.TenantId is null
            ? (await hosts.FindActiveHostUsersAsync(recipients, token).ConfigureAwait(false)).Keys.ToHashSet()
            : (await members.FindActiveTenantMembersAsync(recipients, token).ConfigureAwait(false)).Keys.ToHashSet();
        return await transaction.ExecuteResultAsync(async inner =>
        {
            if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                WorkflowSqlParameters.Create(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Conflict();
            var current = await InstanceAsync(instanceId, scope, inner).ConfigureAwait(false);
            if (current is null || current.StartedById != actor) return Conflict();
            var replay = await query.QuerySingleOrDefaultAsync<WorkflowManualReminderReceiptRecord>(WorkflowManualReminderSql.FindReceipt, parameters, inner).ConfigureAwait(false);
            if (replay is not null) return Replay(replay, actor, hash);
            if (await query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, parameters, inner).ConfigureAwait(false) is not null) return Conflict();
            if (current.StatusKey != "active" || current.Revision != request.ExpectedInstanceRevision || recipients.Any(x => !valid.Contains(x))) return Conflict();
            var actual = (await query.QueryAsync<WorkflowManualReminderTargetRecord>(WorkflowManualReminderSql.ListTargets, parameters, inner).ConfigureAwait(false))
                .Where(x => x.StepId == request.StepId).ToArray();
            if (!actual.SequenceEqual(prepared) || actual.Any(x => x.StepRevision != request.ExpectedStepRevision)) return Conflict();
            var asset = await AssetAsync(current, scope, inner).ConfigureAwait(false);
            if (!Allows(asset, actual[0])) return Fail<WorkflowManualReminderResponse>(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
            parameters["StepId"] = request.StepId;
            var budget = await query.QuerySingleOrDefaultAsync<WorkflowManualReminderBudgetRecord>(WorkflowManualReminderSql.ReadBudget, parameters, inner).ConfigureAwait(false);
            // MySQL datetime(6) 固定到微秒；首次响应与持久回放必须保留完全相同的受理时间。
            var utc = clock.UtcNow.ToUniversalTime();
            var now = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
            if (!WorkflowManualReminderRules.CanRequest(budget?.ManualCount ?? 0, budget?.LastRequestedAtUtc, now))
                return Fail<WorkflowManualReminderResponse>("workflow.instance.reminder_rate_limited", ErrorType.RateLimited);
            var id = ids.NewId();
            var sequence = (budget?.ManualCount ?? 0) + 1;
            await command.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord, WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instanceId),
                ("StepId", request.StepId), ("TodoId", null), ("ActionKey", "remind"), ("ActorUserId", actor), ("InstanceRevision", current.Revision),
                ("IdempotencyKey", request.IdempotencyKey), ("CommentSummary", null), ("CreatedAtUtc", now), ("ResultStatusKey", current.StatusKey), ("ResultTodoId", null)), inner).ConfigureAwait(false);
            parameters["Id"] = id; parameters["InstanceRevision"] = current.Revision; parameters["StepRevision"] = request.ExpectedStepRevision;
            parameters["ManualSequence"] = sequence; parameters["RecipientCount"] = actual.Length; parameters["RequestHash"] = hash; parameters["Now"] = now;
            await command.ExecuteAsync(WorkflowManualReminderSql.Insert, parameters, inner).ConfigureAwait(false);
            var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
            await command.ExecuteAsync(WorkflowSql.InsertExecutionLog, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instanceId),
                ("StepId", request.StepId), ("TransitionKey", "instance.remind"), ("FromStatusKey", current.StatusKey), ("ToStatusKey", current.StatusKey),
                ("IdempotencyKey", request.IdempotencyKey), ("Summary", hash), ("CreatedAtUtc", now), ("CorrelationJson", correlation)), inner).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                ("InstanceId", instanceId), ("OperationKey", "instance.remind"), ("ActorUserId", actor), ("ResourceTypeKey", "step"), ("ResourceId", request.StepId),
                ("OutcomeKey", "accepted"), ("DetailJson", JsonSerializer.Serialize(new WorkflowManualReminderAuditDetail(id, sequence, actual.Length),
                    WorkflowJsonSerializerContext.Default.WorkflowManualReminderAuditDetail)), ("CreatedAtUtc", now), ("CorrelationJson", correlation)), inner).ConfigureAwait(false);
            foreach (var target in actual)
                await publisher.PublishManualReminderAsync(id, instanceId, target.TodoId, actor, target.AssigneeUserId,
                    current.BusinessType, current.BusinessId, sequence, now, inner).ConfigureAwait(false);
            return Result<WorkflowManualReminderResponse>.Success(new(id, request.StepId, current.Revision, request.ExpectedStepRevision, sequence, actual.Length, now));
        }, token).ConfigureAwait(false);
    }

    private static bool Allows(WorkflowRuntimeAssetRecord? asset, WorkflowManualReminderTargetRecord target) =>
        WorkflowHumanActivityKinds.IsFormActivity(target.NodeTypeKey) && asset is not null &&
        WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, target.NodeKey, out var policy) && policy!.Actions.Contains("remind");
    private Task<WorkflowInstanceRecord?> InstanceAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, WorkflowSqlParameters.Create(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowRuntimeAssetRecord?> AssetAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private static Dictionary<string, object?> P(Guid id, Guid actor, WorkflowManagementScope scope) =>
        WorkflowSqlParameters.Create(("InstanceId", id), ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey));
    private static Result<WorkflowManualReminderResponse> Replay(WorkflowManualReminderReceiptRecord receipt, Guid actor, string hash) =>
        receipt.RequestedByUserId == actor && receipt.RequestHash == hash
            ? Result<WorkflowManualReminderResponse>.Success(new(receipt.Id, receipt.StepId, receipt.InstanceRevision, receipt.StepRevision, receipt.ManualSequence, receipt.RecipientCount, receipt.CreatedAtUtc)) : Conflict();
    private static Result<WorkflowManualReminderResponse> Conflict() => Fail<WorkflowManualReminderResponse>(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static Result<T> Fail<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The workflow reminder request is unavailable.", type));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RequestWorkflowManualReminder(Guid StepId, long ExpectedInstanceRevision, long ExpectedStepRevision, string IdempotencyKey);
internal sealed record WorkflowManualReminderResponse(Guid RequestId, Guid StepId, long InstanceRevision, long StepRevision, int ManualSequence, int RecipientCount, DateTimeOffset AcceptedAtUtc);
internal sealed record WorkflowManualReminderTargetResponse(Guid StepId, string NodeKey, string NodeTypeKey, long StepRevision, int ActiveTodoCount,
    int ManualCount, DateTimeOffset? LastRequestedAtUtc, bool CanRemind);
internal sealed record WorkflowManualReminderTargetsResponse(long InstanceRevision, IReadOnlyList<WorkflowManualReminderTargetResponse> Items);
internal sealed record WorkflowManualReminderAuditDetail(Guid RequestId, int ManualSequence, int RecipientCount);
