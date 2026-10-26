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
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>领取前在事务外校验当前候选，实例锁内CAS唯一领取；管理分配入口有独立精确授权。</summary>
internal sealed class WorkflowClaimPoolService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, WorkflowClaimCandidateAuthorizer candidates,
    IHostUserBatchSelectionDirectory hosts, ITenantMemberSelectionDirectory members, WorkflowBranchStateCoordinator branches,
    WorkflowHumanActivityActivationWriter activation, WorkflowNotificationOutboxPublisher notifications,
    IOptions<DatabaseOptions> database)
{
    public async Task<Result<WorkflowClaimPoolPageResponse>> ListAsync(Guid actor, int page, int pageSize, CancellationToken token)
    {
        if (actor == Guid.Empty || page is < 1 or > 100_000 || pageSize is < 1 or > 100)
            return Fail<WorkflowClaimPoolPageResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(tenant);
        var parameters = P(("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey), ("Offset", (page - 1) * pageSize), ("Take", pageSize));
        var count = await query.QuerySingleOrDefaultAsync<int>(WorkflowClaimSql.CountMine, parameters, token).ConfigureAwait(false);
        var rows = await query.QueryAsync<WorkflowClaimPoolListRecord>(database.Value.Provider == DatabaseProvider.SqlServer
            ? WorkflowClaimSql.ListMineSqlServer : WorkflowClaimSql.ListMineMySql, parameters, token).ConfigureAwait(false);
        return Result<WorkflowClaimPoolPageResponse>.Success(new(rows.Select(row => new WorkflowClaimPoolResponse(row.Id,
            row.InstanceId, row.NodeKey, row.DefinitionKey, row.BusinessType, row.BusinessId, row.BusinessTitle,
            row.Revision, row.ClaimCycle, row.CreatedAtUtc, row.DueAtUtc)).ToArray(), count, page, pageSize));
    }

    public Task<Result<WorkflowInstanceResponse>> ClaimAsync(Guid poolId, Guid actor, ClaimWorkflowPoolRequest request, CancellationToken token) =>
        AcquireAsync(poolId, actor, actor, request, false, token);

    public Task<Result<WorkflowInstanceResponse>> AssignAsync(Guid poolId, Guid actor, AssignWorkflowPoolRequest request, CancellationToken token) =>
        AcquireAsync(poolId, actor, request.AssigneeUserId, new(request.ExpectedRevision, request.IdempotencyKey), true, token);

    private async Task<Result<WorkflowInstanceResponse>> AcquireAsync(Guid poolId, Guid actor, Guid target,
        ClaimWorkflowPoolRequest request, bool administrative, CancellationToken token)
    {
        if (poolId == Guid.Empty || actor == Guid.Empty || target == Guid.Empty || request.ExpectedRevision < 1 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        request = request with { IdempotencyKey = request.IdempotencyKey.Trim() };
        var scope = WorkflowManagementScope.Resolve(tenant);
        var pool = await FindPoolAsync(poolId, scope, token).ConfigureAwait(false);
        if (pool is null) return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        var instance = await FindInstanceAsync(pool.InstanceId, scope, token).ConfigureAwait(false);
        if (instance?.FormVersionId is null) return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var action = administrative ? "assign" : "claim";
        var hash = Hash(poolId, actor, target, request, administrative);
        var prior = await FindReceiptAsync(instance.Id, request.IdempotencyKey, token).ConfigureAwait(false);
        if (prior is not null) return await ReplayAsync(pool, instance, actor, action, hash, prior, scope, token).ConfigureAwait(false);
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var draft = asset is null ? null : JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var node = draft?.Nodes.SingleOrDefault(item => item.NodeKey == pool.NodeKey && item.NodeTypeKey == WorkflowHumanActivityKinds.Claim);
        if (node is null || !WorkflowClaimPolicy.TryRead(node.Config, out var policy) || policy!.AllowRelease != pool.AllowRelease ||
            !WorkflowAssigneePolicy.TryRead(node.Config, out var source)) return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var frozen = await query.QueryAsync<WorkflowClaimCandidateRecord>(WorkflowClaimSql.ListCandidates,
            P(("PoolId", pool.Id), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (!administrative && !frozen.Any(candidate => candidate.UserId == actor))
            return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        WorkflowAssigneeResolutionSnapshot? resolution = null;
        if (!administrative && (source.Handling is not null || source.Sources.Any(item => item.ResolverKindKey is WorkflowAssigneePolicy.FormUsers or WorkflowAssigneePolicy.FormUnits or WorkflowAssigneePolicy.PreviousActors or WorkflowAssigneePolicy.StarterSelectedUsers or WorkflowAssigneePolicy.FlowAdministrators)))
        {
            var snapshot = await query.QuerySingleOrDefaultAsync<WorkflowAssigneeSnapshotRecord>(WorkflowDynamicAssigneeSql.FindClaimSnapshot,
                P(("PoolId", pool.Id), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (snapshot is null || snapshot.AssigneeSnapshotJson.Length > 128 * 1024 || snapshot.AssigneeSnapshotHash !=
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.AssigneeSnapshotJson))))
                return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            resolution = JsonSerializer.Deserialize(snapshot.AssigneeSnapshotJson, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
            if (resolution is null || resolution.SchemaVersion != 1 || resolution.Sources is null || resolution.MemberUserIds is null ||
                resolution.InitiatorUserId != instance.StartedById || resolution.MemberUserIds.Count is < 1 or > 20 ||
                !resolution.MemberUserIds.ToHashSet().SetEquals(frozen.Select(candidate => candidate.UserId)))
                return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }
        var current = administrative
            ? scope.TenantId is null
                ? (await hosts.FindActiveHostUsersAsync([target], token).ConfigureAwait(false)).ContainsKey(target)
                : (await members.FindActiveTenantMembersAsync([target], token).ConfigureAwait(false)).ContainsKey(target)
            : await candidates.IsCurrentCandidateAsync(source!, scope, instance.StartedById, actor, token, resolution).ConfigureAwait(false);
        if (!administrative && !frozen.Any(candidate => candidate.UserId == actor) || !current)
            return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        return await transaction.ExecuteResultAsync(async inner =>
        {
            if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                P(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Conflict();
            var actual = await FindPoolAsync(poolId, scope, inner).ConfigureAwait(false);
            var owned = await FindInstanceAsync(instance.Id, scope, inner).ConfigureAwait(false);
            if (actual is null || owned?.FormVersionId is null) return Conflict();
            var receipt = await FindReceiptAsync(owned.Id, request.IdempotencyKey, inner).ConfigureAwait(false);
            if (receipt is not null) return await ReplayAsync(actual, owned, actor, action, hash, receipt, scope, inner).ConfigureAwait(false);
            if (owned.StatusKey != "active" || actual.StatusKey != "available" || actual.Revision != request.ExpectedRevision ||
                actual.ClaimCycle is < 0 or >= WorkflowClaimPolicy.MaximumClaimCycles ||
                !administrative && !WorkflowClaimRules.CanClaim(actor, frozen.Select(candidate => candidate.UserId).ToArray(),
                    [actor], actual.StatusKey, actual.Revision, request.ExpectedRevision, actual.ClaimCycle)) return Conflict();
            var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
                P(("InstanceId", owned.Id), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
            submission = await branches.ReadAsync(owned.Id, actual.ParallelJoinId, actual.ParallelBranchKey,
                submission, scope.TenantScopeKey, inner).ConfigureAwait(false);
            var values = submission is null ? null : JsonSerializer.Deserialize(submission.SubmissionJson,
                WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (values is null) return Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            var now = clock.UtcNow; var todo = ids.NewId();
            var nextSignal = Min(actual.NextReminderAtUtc, actual.EscalatedAtUtc is null ? actual.EscalateAtUtc : null);
            var parameters = P(("PoolId", actual.Id), ("InstanceId", owned.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("TodoId", todo), ("AssigneeUserId", target), ("ActorUserId", actor), ("Revision", actual.Revision),
                ("StepRevision", actual.StepRevision), ("Id", ids.NewId()), ("Now", now), ("NextTimeoutSignalAtUtc", nextSignal),
                ("AuthorizationKey", administrative ? WorkflowPermissions.ClaimPoolsAssign : WorkflowPermissions.ClaimPoolsClaim));
            if (await command.ExecuteAsync(WorkflowClaimSql.InsertTodo, parameters, inner).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowClaimSql.ClaimPool, parameters, inner).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowClaimSql.InsertAttempt, parameters, inner).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowClaimSql.AssignStep, parameters, inner).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                    P(("Id", owned.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", owned.Revision)), inner).ConfigureAwait(false) != 1) return Conflict();
            await activation.CaptureInputsAsync(new(actual.Id, todo, [todo]), owned.Id, scope.TenantScopeKey, values, inner).ConfigureAwait(false);
            await WriteJournalAsync(actual, owned, actor, todo, action, request.IdempotencyKey, hash, now, scope, inner).ConfigureAwait(false);
            await notifications.PublishTodoAssignedAsync(owned.Id, todo, target, owned.BusinessType, owned.BusinessId, now, inner).ConfigureAwait(false);
            return Result<WorkflowInstanceResponse>.Success(Map(owned, owned.Revision + 1, todo));
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<WorkflowInstanceResponse>> ReplayAsync(WorkflowClaimPoolRecord pool, WorkflowInstanceRecord instance,
        Guid actor, string action, string hash, WorkflowActionReceiptRecord receipt, WorkflowManagementScope scope, CancellationToken token)
    {
        if (receipt.ActorUserId != actor || receipt.ActionKey != action || receipt.RequestHash != hash || receipt.ResultStatusKey != "active" ||
            action != "release" && receipt.ResultTodoId is null ||
            await query.QuerySingleOrDefaultAsync<int>(WorkflowClaimSql.CountReceiptForPool,
                P(("PoolId", pool.Id), ("InstanceId", instance.Id), ("ActorUserId", actor),
                    ("IdempotencyKey", receipt.IdempotencyKey), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1) return Conflict();
        return Result<WorkflowInstanceResponse>.Success(Map(instance, receipt.InstanceRevision, receipt.ResultTodoId));
    }

    internal async Task WriteJournalAsync(WorkflowClaimPoolRecord pool, WorkflowInstanceRecord instance, Guid actor, Guid todo,
        string action, string key, string hash, DateTimeOffset now, WorkflowManagementScope scope, CancellationToken token)
    {
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await command.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            P(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", pool.Id), ("TodoId", todo), ("ActionKey", action),
                ("ActorUserId", actor), ("InstanceRevision", instance.Revision + 1), ("IdempotencyKey", key), ("CommentSummary", null),
                ("CreatedAtUtc", now), ("ResultStatusKey", "active"), ("ResultTodoId", action == "release" ? null : todo)), token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            P(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", pool.Id), ("TransitionKey", "pool." + action),
                ("FromStatusKey", "active"), ("ToStatusKey", "active"), ("IdempotencyKey", key), ("Summary", hash),
                ("CreatedAtUtc", now), ("CorrelationJson", correlation)), token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            P(("Id", ids.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey), ("InstanceId", instance.Id),
                ("OperationKey", "pool." + action), ("ActorUserId", actor), ("ResourceTypeKey", "claim_pool"), ("ResourceId", pool.Id),
                ("OutcomeKey", "succeeded"), ("DetailJson", "{}"), ("CreatedAtUtc", now), ("CorrelationJson", correlation)), token).ConfigureAwait(false);
    }

    private Task<WorkflowClaimPoolRecord?> FindPoolAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowClaimPoolRecord>(WorkflowClaimSql.FindPool, P(("PoolId", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowInstanceRecord?> FindInstanceAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, P(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowActionReceiptRecord?> FindReceiptAsync(Guid instance, string key, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, P(("InstanceId", instance), ("IdempotencyKey", key)), token);
    private static string Hash(Guid pool, Guid actor, Guid target, ClaimWorkflowPoolRequest request, bool administrative) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{pool:D}|{actor:D}|{target:D}|{request.ExpectedRevision}|{administrative}|{request.IdempotencyKey}")));
    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right) => left is null ? right : right is null ? left : left <= right ? left : right;
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<T> Fail<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The workflow pool action is not available.", type));
    private static Result<WorkflowInstanceResponse> Conflict() => Fail<WorkflowInstanceResponse>(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static WorkflowInstanceResponse Map(WorkflowInstanceRecord instance, long revision, Guid? todo) =>
        new(instance.Id, instance.DefinitionVersionId, instance.FormVersionId!.Value, instance.BusinessType, instance.BusinessId,
            instance.BusinessTitle, "active", revision, todo, instance.StartedAtUtc);
}

/// <summary>领取不接受请求声明的用户、租户或主表单修订。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClaimWorkflowPoolRequest(long ExpectedRevision, string IdempotencyKey);
/// <summary>管理员分配是独立权限入口，不能通过普通领取请求覆盖候选身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AssignWorkflowPoolRequest(long ExpectedRevision, Guid AssigneeUserId, string IdempotencyKey);
internal sealed record WorkflowClaimPoolResponse(Guid Id, Guid InstanceId, string NodeKey, string DefinitionKey, string BusinessType,
    string BusinessId, string? BusinessTitle, long Revision, int ClaimCycle, DateTimeOffset CreatedAtUtc, DateTimeOffset? DueAtUtc);
internal sealed record WorkflowClaimPoolPageResponse(IReadOnlyList<WorkflowClaimPoolResponse> Items, int TotalCount, int Page, int PageSize);
/// <summary>本人待办的领取轮次与释放能力，不公开其他候选或后续办理人。</summary>
internal sealed record WorkflowClaimProgressResponse(string PoolStatusKey, long PoolRevision, int ClaimCycle, bool AllowRelease);
