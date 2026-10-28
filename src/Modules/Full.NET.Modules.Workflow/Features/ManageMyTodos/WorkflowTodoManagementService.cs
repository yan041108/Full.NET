using Full.NET.Abstractions.Results;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>只读取可信作用域内当前认证用户的有界活动待办。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="transaction">Workflow 本地事务边界。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="databaseOptions">数据库提供程序配置。</param>
/// <param name="approvalPreparation">事务外办理人快照准备器。</param>
/// <param name="notificationPublisher">工作流提醒事务 Outbox 发布器。</param>
/// <param name="transitionExecutor">审批迁移执行器。</param>
/// <param name="parallelJoinCoordinator">并行汇合协调器。</param>
internal sealed partial class WorkflowTodoManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    WorkflowHumanActivityPreparation approvalPreparation,
    WorkflowNotificationOutboxPublisher notificationPublisher,
    WorkflowTodoCountersignService countersignService,
    WorkflowRuntimeTransitionExecutor transitionExecutor,
    WorkflowParallelJoinCoordinator parallelJoinCoordinator,
    WorkflowFormAttachmentCoordinator attachmentCoordinator,
    WorkflowBranchStateCoordinator branchStates,
    WorkflowSignatureCoordinator signatureCoordinator,
    WorkflowApprovalStepResolutionCoordinator stepResolution,
    WorkflowTaskDecisionCoordinator taskDecisions,
    WorkflowHumanActivityCompletionCoordinator resultCallbacks)
{
    public Task<Result<WorkflowInstanceResponse>> ApproveAsync(Guid todoId, Guid actorUserId,
        ActWorkflowTodoRequest request, CancellationToken cancellationToken = default) =>
        ExecutePreparedAsync(todoId, actorUserId, request, "approve",
            (prepared, token) => ActAsync(todoId, actorUserId, request, "approve", prepared, token), cancellationToken);

    private async Task<Result<WorkflowInstanceResponse>> ExecutePreparedAsync(Guid todoId, Guid actorUserId,
        ActWorkflowTodoRequest request, string actionKey,
        Func<WorkflowActionPreparation, CancellationToken, Task<Result<WorkflowInstanceResponse>>> execute,
        CancellationToken token)
    {
        var prepared = await PrepareActionAsync(todoId, actorUserId, request, actionKey, token).ConfigureAwait(false);
        if (!prepared.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(prepared.Error!);
        try
        {
            return await transaction.ExecuteResultAsync(async inner =>
            {
                // 定位来自事务外本人 Todo；锁后由办理核心重读身份、修订与回执，不能消费准备阶段的业务状态。
                if (prepared.Value!.InstanceId is { } instanceId &&
                    await commandExecutor.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                        WorkflowSqlParameters.Create(("Id", instanceId),
                            ("TenantScopeKey", WorkflowManagementScope.Resolve(currentTenant).TenantScopeKey)), inner).ConfigureAwait(false) != 1)
                    return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
                return await execute(prepared.Value, inner).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            await signatureCoordinator.FinishAsync(prepared.Value!.Signature, token).ConfigureAwait(false);
            await attachmentCoordinator.FinishAsync(prepared.Value.Attachments, token).ConfigureAwait(false);
        }
    }

    // 准备阶段只读取当前快照；事务内仍重新检查身份、版本、字段策略和幂等收据。
    private async Task<Result<WorkflowActionPreparation>> PrepareActionAsync(Guid todoId, Guid actorUserId,
        ActWorkflowTodoRequest request, string actionKey, CancellationToken token)
    {
        if (request.ExpectedRevision < 1 || request.ExpectedFormRevision < 1 || request.FieldPatch.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 || request.Comment is { Length: > 512 })
            return Result<WorkflowActionPreparation>.Success(WorkflowActionPreparation.Empty);
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var empty = WorkflowActionPreparation.Empty with { InstanceId = todo?.AssigneeUserId == actorUserId ? todo.InstanceId : null };
        if (todo is null || todo.AssigneeUserId != actorUserId || todo.StatusKey != "active" || todo.Revision != request.ExpectedRevision)
            return Result<WorkflowActionPreparation>.Success(empty);
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.StatusKey != "active" || instance.FormVersionId is null) return Result<WorkflowActionPreparation>.Success(empty);
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        submission = await branchStates.ReadAsync(todo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
        if (asset is null || submission is null || submission.Revision != request.ExpectedFormRevision)
            return Result<WorkflowActionPreparation>.Success(empty);
        if (!WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var actionPolicy))
            return Result<WorkflowActionPreparation>.Failure(new Error(WorkflowErrorCodes.SchemaInvalid, "The node action policy is invalid.", ErrorType.Validation));
        var allowed = actionPolicy!.Validate(actionKey, request.Comment, request.SignatureFileId);
        if (!allowed.IsSuccess) return Result<WorkflowActionPreparation>.Failure(allowed.Error!);
        if (actionKey == "return" && await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(
            WorkflowTodoDelegationSql.FindForProxy, WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Result<WorkflowActionPreparation>.Failure(new(WorkflowErrorCodes.TodoActionForbidden,
                "A proxy cannot change the delegated activity route.", ErrorType.Forbidden));
        if (actionKey == "return" && await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(
            WorkflowSql.FindCountersignItemByTodoId, WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Result<WorkflowActionPreparation>.Failure(new(WorkflowErrorCodes.TodoActionForbidden,
                "A supplemental signer cannot change the activity route.", ErrorType.Forbidden));
        var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (draft is null || schema is null || !WorkflowRuntimePlan.TryCreate(draft, schema, out var plan))
            return Result<WorkflowActionPreparation>.Success(empty);
        var patched = BuildPatchedSubmission(asset.CanonicalJson, todo.NodeKey, asset.FormSchemaJson,
            submission.SubmissionJson, request.FieldPatch);
        if (patched is null) return Result<WorkflowActionPreparation>.Success(empty);
        var approvals = WorkflowPreparedHumanActivities.Empty;
        if (actionKey is "approve" or "complete")
        {
            var values = JsonSerializer.Deserialize(patched, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (values is not null)
            {
                if (!plan!.TryGetResultCallbackPolicy(todo.NodeKey, out var callback)) return Result<WorkflowActionPreparation>.Success(empty);
                if (callback is not null) values = callback.Apply(values, true);
            }
            if (values is null || !plan!.TryResolveAfterHuman(todo.NodeKey, values, todo.ParallelJoinNodeKey, out var transition))
                return Result<WorkflowActionPreparation>.Success(empty);
            approvals = await approvalPreparation.PrepareAsync(transition, scope, instance.StartedById, token, values, instance.Id, todo.NodeKey,
                flowAdministratorUserIds: plan.FlowAdministratorUserIds, executionEpochId: await WorkflowExecutionEpochScope.ReadForStepAsync(queryExecutor, instance.Id, scope.TenantScopeKey, todo.StepId, token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        var previousValues = JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        var nextValues = JsonSerializer.Deserialize(patched, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (previousValues is null || nextValues is null ||
            !WorkflowNodeFieldPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, schema, out var fields))
            return Result<WorkflowActionPreparation>.Success(empty);
        var visibleFiles = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, fields!.CreateView(schema, previousValues).Values)
            .Values.SelectMany(files => files).ToHashSet();
        var attachments = await attachmentCoordinator.PrepareAsync(actorUserId, submission.Id, instance.Id, scope.TenantScopeKey,
            schema, previousValues, nextValues, visibleFiles, submission.Revision, patched, token).ConfigureAwait(false);
        if (!attachments.IsSuccess) return Result<WorkflowActionPreparation>.Failure(attachments.Error!);
        try
        {
            var signature = await signatureCoordinator.PrepareAsync(todo, instance, actorUserId, actionKey,
                request.IdempotencyKey, request.SignatureFileId, request.ExpectedFormRevision + 1, patched, token).ConfigureAwait(false);
            if (!signature.IsSuccess)
            {
                await attachmentCoordinator.FinishAsync(attachments.Value, token).ConfigureAwait(false);
                return Result<WorkflowActionPreparation>.Failure(signature.Error!);
            }
            return Result<WorkflowActionPreparation>.Success(new(approvals, signature.Value, attachments.Value) { InstanceId = instance.Id });
        }
        catch
        {
            await attachmentCoordinator.FinishAsync(attachments.Value, token).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Result<WorkflowTodoDetailResponse>> GetAsync(
        Guid todoId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
            WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (todo is null)
        {
            return DetailFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        if (todo.AssigneeUserId != actorUserId)
        {
            return DetailFailure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var asset = instance is null
            ? null
            : await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
                WorkflowSql.FindRuntimeAsset,
                WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                    ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
            WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        submission = await branchStates.ReadAsync(todo, submission, scope.TenantScopeKey, cancellationToken).ConfigureAwait(false);
        if (todo.StatusKey != "active" && submission is not null)
        {
            var historyView = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoViewRecord>(WorkflowTodoViewSql.FindOwned,
                WorkflowSqlParameters.Create(("TodoId", todo.Id), ("ActorUserId", actorUserId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
            if (historyView is null) return DetailFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
            submission = submission with { SubmissionJson = historyView.OutputJson ?? historyView.InputJson, Revision = historyView.OutputRevision ?? historyView.InputRevision };
        }
        if (instance is null || asset is null || submission is null)
        {
            return DetailFailure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var schema = JsonSerializer.Deserialize(
            asset.FormSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = JsonSerializer.Deserialize(
            submission.SubmissionJson,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (schema is null || values is null ||
            !WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var actions) ||
            !WorkflowNodeFieldPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, schema, out var policy))
        {
            return DetailFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }

        var view = policy!.CreateView(schema, values);
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Vote)
            view = view with { FieldPolicies = view.FieldPolicies.ToDictionary(item => item.Key, _ => "readOnly", StringComparer.Ordinal) };
        var visibleSchema = JsonSerializer.SerializeToElement(
            view.Schema,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var visibleSubmission = JsonSerializer.SerializeToElement(
            view.Values,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        var tally = todo.ApprovalModeKey is null
            ? null
            : await queryExecutor.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(
                todo.ApprovalModeKey == "group" ? GroupTallyStatement : ApprovalTallyStatement,
                WorkflowSqlParameters.Create(("StepId", todo.StepId), ("InstanceId", todo.InstanceId)), cancellationToken)
                .ConfigureAwait(false);
        var supplemental = WorkflowHumanActivityKinds.IsApproval(todo.NodeTypeKey)
            ? await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
                WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false) : null;
        IReadOnlyList<WorkflowApprovalGroupProgressResponse> groupProgress = [];
        if (todo.ApprovalModeKey == "group")
        {
            var rows = await queryExecutor.QueryAsync<WorkflowApprovalGroupProgressRecord>(
                databaseOptions.Value.Provider == DatabaseProvider.SqlServer ? WorkflowGroupApprovalSql.ListProgressSqlServer : WorkflowGroupApprovalSql.ListProgressMySql,
                WorkflowSqlParameters.Create(("TodoId", todo.Id), ("StepId", todo.StepId), ("InstanceId", todo.InstanceId),
                    ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
            if (supplemental is not null && todo.StatusKey == "active")
            {
                // 补充办理人不占新席位；当前组从链的原待办读取，不能按补充人员碰巧所在的其他组推断。
                var originGroup = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowApprovalGroupContextRecord>(WorkflowGroupApprovalSql.FindHistoryForTodo,
                    WorkflowSqlParameters.Create(("TodoId", supplemental.OriginTodoId), ("StepId", todo.StepId),
                        ("InstanceId", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
                if (originGroup is null) return DetailFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
                rows = rows.Select(row => row with { CurrentGroup = row.Id == originGroup.Id ? 1 : 0 }).ToArray();
            }
            if (rows.Count is < 2 or > 8 || rows.Any(row => (row.ModeKey == "system"
                ? row.SlotCount != 0 || row.RequiredApprovalCount != 0 || row.StatusKey != "approved" || row.CurrentGroup != 0
                : row.SlotCount is < 1 or > 20 || row.RequiredApprovalCount < 1 || row.RequiredApprovalCount > row.SlotCount) ||
                row.ApprovedCount < 0 || row.RejectedCount < 0 || row.PendingCount < 0 || row.CancelledCount < 0 ||
                row.ApprovedCount + row.RejectedCount + row.PendingCount + row.CancelledCount != row.SlotCount))
                return DetailFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            groupProgress = rows.Select(row => new WorkflowApprovalGroupProgressResponse(row.Id, row.GroupKey, row.GroupName,
                row.SequenceNo, row.ModeKey, row.RequiredApprovalCount, row.SlotCount, (int)row.ApprovedCount,
                (int)row.RejectedCount, (int)row.PendingCount, (int)row.CancelledCount, row.StatusKey, row.CurrentGroup == 1)).ToArray();
        }
        WorkflowTaskProgressResponse? taskProgress = null;
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Task)
        {
            var task = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTaskContextRecord>(WorkflowTaskSql.FindHistoryForTodo,
                WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TaskId", todo.StepId), ("InstanceId", todo.InstanceId),
                    ("ActorUserId", actorUserId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
            var counts = task is null ? null : await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTaskTallyRecord>(
                databaseOptions.Value.Provider == DatabaseProvider.SqlServer ? WorkflowTaskSql.TallySqlServer : WorkflowTaskSql.TallyMySql,
                WorkflowSqlParameters.Create(("TaskId", todo.StepId), ("InstanceId", todo.InstanceId)), cancellationToken).ConfigureAwait(false);
            if (task is null || new WorkflowTaskPolicy(task.CompletionModeKey).RequiredCompletions(task.MemberCount) != task.RequiredCompletionCount ||
                task.RequiredCompletionCount < 1 || counts is null || counts.CompletedCount < 0 || counts.PendingCount < 0 || counts.CancelledCount < 0 ||
                counts.CompletedCount + counts.PendingCount + counts.CancelledCount != task.MemberCount)
                return DetailFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            taskProgress = new(task.CompletionModeKey, task.MemberCount, task.RequiredCompletionCount,
                (int)counts.CompletedCount, (int)counts.PendingCount, (int)counts.CancelledCount);
        }
        WorkflowClaimProgressResponse? claim = null;
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Claim)
        {
            var pool = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowClaimPoolRecord>(WorkflowClaimSql.FindPool,
                WorkflowSqlParameters.Create(("PoolId", todo.StepId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
            if (pool is null || pool.InstanceId != todo.InstanceId) return DetailFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            var cycle = await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowClaimSql.FindHistoryCycle,
                WorkflowSqlParameters.Create(("PoolId", pool.Id), ("InstanceId", todo.InstanceId), ("TodoId", todo.Id),
                    ("ActorUserId", actorUserId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
            if (cycle is < 1 or > WorkflowClaimPolicy.MaximumClaimCycles) return DetailFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            claim = new(pool.StatusKey, pool.Revision, cycle, todo.StatusKey == "active" && pool.StatusKey == "claimed" &&
                pool.CurrentTodoId == todo.Id && pool.CurrentAssigneeUserId == actorUserId && pool.AllowRelease && cycle < WorkflowClaimPolicy.MaximumClaimCycles);
        }
        WorkflowVoteDetail? vote = null;
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Vote)
        {
            var voteDetail = await ReadVoteDetailAsync(todo, instance, asset, scope, actorUserId, cancellationToken).ConfigureAwait(false);
            if (!voteDetail.IsSuccess) return Result<WorkflowTodoDetailResponse>.Failure(voteDetail.Error!);
            vote = voteDetail.Value;
        }
        var isApproval = WorkflowHumanActivityKinds.IsApproval(todo.NodeTypeKey);
        var delegation = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        return Result<WorkflowTodoDetailResponse>.Success(new(
            todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
            todo.StatusKey, todo.Revision, asset.FormVersionId,
            visibleSchema, visibleSubmission, view.FieldPolicies,
            submission.Revision)
        {
            ActivityKindKey = todo.NodeTypeKey,
            TaskProgress = taskProgress,
            VoteForm = vote?.Form,
            VoteProgress = vote?.Progress,
            ClaimProgress = claim,
            ApprovalGroups = groupProgress,
            AllowedActions = actions!.Actions.Where(action => (action != "release" || claim?.AllowRelease == true) &&
                (supplemental is null || action is "approve" or "reject" or "remind") &&
                (delegation is null || action is "approve" or "reject" or "complete" or "vote" or "remind")).Order(StringComparer.Ordinal).ToArray(),
            SignRequired = actions.SignRequired,
            CommentRequired = actions.CommentRequired,
            ApprovalModeKey = isApproval ? todo.ApprovalModeKey ?? "single" : "none",
            RequiredApprovalCount = isApproval ? todo.RequiredApprovalCount ?? 1 : 0,
            ApprovedCount = isApproval ? (int)(tally?.ApprovedCount ?? 0) : 0,
            RejectedCount = isApproval ? (int)(tally?.RejectedCount ?? 0) : 0,
            PendingCount = isApproval ? (int)(tally?.PendingCount ?? 1) : 0,
        });
    }

    public async Task<Result<WorkflowTodoRuntimeResponse>> GetRuntimeAsync(
        Guid todoId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var result = await GetAsync(todoId, actorUserId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result<WorkflowTodoRuntimeResponse>.Failure(result.Error!);
        }

        var detail = result.Value!;
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var version = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(WorkflowSql.FindFormVersionById,
            WorkflowSqlParameters.Create(("Id", detail.FormVersionId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        JsonElement? webRender = null;
        if (version is not null)
        {
            using var json = JsonDocument.Parse(version.WebRenderSchemaJson);
            if (json.RootElement.TryGetProperty("widgetList", out _))
            {
                var bindings = await WorkflowDesignerRelease.ReadBindingsAsync(queryExecutor, detail.FormVersionId, scope, cancellationToken).ConfigureAwait(false);
                if (!WorkflowDesignerRelease.Matches(bindings)) return Result<WorkflowTodoRuntimeResponse>.Failure(new Error(WorkflowErrorCodes.FormExtensionForbidden, "The published renderer is unavailable.", ErrorType.Conflict));
                webRender = WorkflowDesignerAsset.CreateRuntimeView(version.WebRenderSchemaJson, detail.FieldPolicies);
                if (webRender is null) return Result<WorkflowTodoRuntimeResponse>.Failure(new Error(WorkflowErrorCodes.FormExtensionForbidden, "The form contains dependencies outside the authorized field view.", ErrorType.Validation));
                webRender = WorkflowDesignerRelease.AttachAuthorizedBindings(webRender.Value, bindings!, detail.FieldPolicies);
            }
        }
        return Result<WorkflowTodoRuntimeResponse>.Success(new(
            detail.Id, detail.InstanceId, detail.StepId, detail.AssigneeUserId,
            detail.StatusKey, detail.Revision, detail.FormVersionId,
            HashUtf8(detail.FormSchema.GetRawText()), detail.FormSchema,
            detail.Submission, detail.FieldPolicies, detail.SubmissionRevision)
        {
            WebRenderJson = webRender,
            ApprovalGroups = detail.ApprovalGroups,
            AllowedActions = detail.AllowedActions,
            SignRequired = detail.SignRequired,
            CommentRequired = detail.CommentRequired,
            ActivityKindKey = detail.ActivityKindKey,
            TaskProgress = detail.TaskProgress,
            VoteForm = detail.VoteForm,
            VoteProgress = detail.VoteProgress,
            ClaimProgress = detail.ClaimProgress,
            ApprovalModeKey = detail.ApprovalModeKey,
            RequiredApprovalCount = detail.RequiredApprovalCount,
            ApprovedCount = detail.ApprovedCount,
            RejectedCount = detail.RejectedCount,
            PendingCount = detail.PendingCount,
        });
    }

    /// <summary>列出当前办理人可以选择的历史人工审批退回目标。</summary>
    /// <param name="todoId">当前活动待办标识。</param>
    /// <param name="actorUserId">当前认证用户标识。</param>
    /// <param name="page">从一开始的页码。</param>
    /// <param name="pageSize">单页候选数，限制为 1～100。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>按最近完成顺序排列的合法退回目标，或稳定业务错误。</returns>
    public async Task<Result<IReadOnlyList<WorkflowTodoReturnTargetResponse>>> ListReturnTargetsAsync(
        Guid todoId,
        Guid actorUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var configuredError = await GetConfiguredActionErrorAsync(todoId, actorUserId, "return", cancellationToken).ConfigureAwait(false);
        if (configuredError is not null) return Result<IReadOnlyList<WorkflowTodoReturnTargetResponse>>.Failure(configuredError);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (long)(page - 1) * pageSize;
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
            WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (todo is null)
        {
            return ReturnTargetsFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        if (todo.AssigneeUserId != actorUserId)
        {
            return ReturnTargetsFailure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        if (todo.StatusKey != "active" || instance?.StatusKey != "active")
        {
            return ReturnTargetsFailure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        }

        var state = await new WorkflowReturnScopeReader(queryExecutor, databaseOptions).ReadAsync(instance, todo.StepId, cancellationToken).ConfigureAwait(false);
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var definition = asset is null ? null : JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var schema = asset is null ? null : JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (state is null || definition is null || schema is null || !WorkflowRuntimePlan.TryCreate(definition, schema, out var plan))
            return Result<IReadOnlyList<WorkflowTodoReturnTargetResponse>>.Success([]);
        var allowed = state.Bindings.Select(binding => binding.StepId).Distinct()
            .Where(id => state.TrySelect(instance, plan!, todo.StepId, id, out _)).ToArray();
        if (allowed.Length == 0 || offset > int.MaxValue) return Result<IReadOnlyList<WorkflowTodoReturnTargetResponse>>.Success([]);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.SqlServer
            ? WorkflowSql.ListTodoReturnTargetsSqlServer : WorkflowSql.ListTodoReturnTargetsMySql;
        var rows = await queryExecutor.QueryAsync<WorkflowTodoReturnTargetRecord>(statement,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("AncestorStepIds", allowed), ("Offset", 0L), ("PageSize", WorkflowDefinitionCompiler.MaximumNodes)), cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<WorkflowTodoReturnTargetResponse>>.Success(rows.Where(row => allowed.Contains(row.StepId))
            .Skip((int)offset).Take(pageSize).Select(row => new WorkflowTodoReturnTargetResponse(row.StepId, row.NodeKey, row.AssigneeUserId, row.CompletedAtUtc)).ToArray());
    }

    /// <summary>把当前待办退回到服务端复核后的历史人工审批节点。</summary>
    /// <param name="todoId">当前活动待办标识。</param>
    /// <param name="actorUserId">当前认证用户标识。</param>
    /// <param name="request">目标步骤、并发修订、字段补丁、原因和幂等键。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>退回后的活动实例快照或稳定业务错误。</returns>
    public Task<Result<WorkflowInstanceResponse>> ReturnAsync(
        Guid todoId,
        Guid actorUserId,
        ReturnWorkflowTodoRequest request,
        CancellationToken cancellationToken = default) =>
        ExecutePreparedAsync(todoId, actorUserId,
            new(request.ExpectedRevision, request.FieldPatch, request.Comment, request.IdempotencyKey,
                request.ExpectedFormRevision, request.SignatureFileId), "return",
            (prepared, token) => ReturnCoreAsync(todoId, actorUserId, request, prepared, token), cancellationToken);

    /// <summary>读取当前待办关联的活动加签链。</summary>
    public async Task<Result<WorkflowTodoCountersignChainResponse>> GetCountersignChainAsync(
        Guid todoId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var error = await GetConfiguredActionErrorAsync(todoId, actorUserId, "countersign", cancellationToken).ConfigureAwait(false);
        if (error is not null) return Result<WorkflowTodoCountersignChainResponse>.Failure(error);
        return await countersignService.GetChainAsync(todoId, actorUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>对活动待办发起前加签或后加签。</summary>
    public async Task<Result<WorkflowInstanceResponse>> CountersignAsync(
        Guid todoId,
        Guid actorUserId,
        CountersignWorkflowTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await GetConfiguredActionErrorAsync(todoId, actorUserId, "countersign", cancellationToken).ConfigureAwait(false);
        if (error is not null) return Result<WorkflowInstanceResponse>.Failure(error);
        return await countersignService.CountersignAsync(todoId, actorUserId, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>取消尚未完成的活动加签链。</summary>
    public async Task<Result<WorkflowInstanceResponse>> CancelCountersignAsync(
        Guid todoId,
        Guid actorUserId,
        CancelWorkflowTodoCountersignRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await GetConfiguredActionErrorAsync(todoId, actorUserId, "countersign", cancellationToken).ConfigureAwait(false);
        if (error is not null) return Result<WorkflowInstanceResponse>.Failure(error);
        return await countersignService.CancelAsync(todoId, actorUserId, request, cancellationToken).ConfigureAwait(false);
    }

    // 节点版本不可变；附加操作先检查资源配置，实际加签事务仍复核身份、状态与修订。
    private async Task<Error?> GetConfiguredActionErrorAsync(Guid todoId, Guid actorId, string actionKey, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (todo is null) return new Error(WorkflowErrorCodes.TodoNotActive, "The workflow todo was not found.", ErrorType.NotFound);
        if (todo.AssigneeUserId != actorId) return new Error(WorkflowErrorCodes.TodoForbidden, "The workflow todo belongs to another actor.", ErrorType.Forbidden);
        if (actionKey is not ("approve" or "reject" or "remind") && await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(
            WorkflowSql.FindCountersignItemByTodoId, WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return new(WorkflowErrorCodes.TodoActionForbidden, "A supplemental signer cannot manage the original activity.", ErrorType.Forbidden);
        if (actionKey is not ("approve" or "reject" or "complete" or "vote" or "remind") &&
            await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
                WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return new(WorkflowErrorCodes.TodoActionForbidden, "A proxy cannot manage the original activity.", ErrorType.Forbidden);
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var asset = instance is null ? null : await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || !WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var policy))
            return new Error(WorkflowErrorCodes.SchemaInvalid, "The node action policy is unavailable.", ErrorType.Validation);
        return policy!.Actions.Contains(actionKey) ? null : new Error(WorkflowErrorCodes.TodoActionForbidden, "The published node does not allow this action.", ErrorType.Forbidden);
    }

    public Task<Result<WorkflowInstanceResponse>> RejectAsync(
        Guid todoId,
        Guid actorUserId,
        ActWorkflowTodoRequest request,
        CancellationToken cancellationToken = default) =>
        ExecutePreparedAsync(todoId, actorUserId, request, "reject",
            (prepared, token) => ActAsync(todoId, actorUserId, request, "reject", prepared, token), cancellationToken);

    /// <summary>执行待办动作，并在批准迁移时同步写入沿途抄送。</summary>
    /// <param name="todoId">待办标识。</param>
    /// <param name="actorUserId">执行动作的当前用户标识。</param>
    /// <param name="request">包含并发版本、幂等键和字段补丁的动作请求。</param>
    /// <param name="actionKey">稳定动作码，只允许批准或驳回。</param>
    /// <param name="token">取消当前异步操作的令牌。</param>
    /// <returns>迁移后的实例状态或稳定业务错误。</returns>
    private async Task<Result<WorkflowInstanceResponse>> ActAsync(
        Guid todoId,
        Guid actorUserId,
        ActWorkflowTodoRequest request,
        string actionKey,
        WorkflowActionPreparation preparation,
        CancellationToken token)
    {
        if (request.ExpectedRevision < 1 || request.ExpectedFormRevision < 1 || request.FieldPatch.ValueKind != System.Text.Json.JsonValueKind.Object ||
            request.IdempotencyKey?.Trim() is not { Length: >= 1 and <= 128 } ||
            request.Comment is { Length: > 512 })
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
            WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)),
            token).ConfigureAwait(false);
        if (todo is null)
        {
            return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        if (todo.AssigneeUserId != actorUserId)
        {
            return Failure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var requestHash = HashRequest(actionKey, request);
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("IdempotencyKey", request.IdempotencyKey.Trim())), token).ConfigureAwait(false);
        if (receipt is not null)
        {
            if (receipt.TodoId != todo.Id || receipt.ActionKey != actionKey || receipt.ActorUserId != actorUserId ||
                receipt.RequestHash != requestHash)
            {
                return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            if (receipt.ResultStatusKey is not null)
            {
                return Result<WorkflowInstanceResponse>.Success(new(
                    instance.Id, instance.DefinitionVersionId, formVersionId,
                    instance.BusinessType, instance.BusinessId, instance.BusinessTitle, receipt.ResultStatusKey,
                    receipt.InstanceRevision, receipt.ResultTodoId, instance.StartedAtUtc));
            }

            var replayTodo = instance.StatusKey == "active"
                ? await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                    WorkflowSql.FindActiveTodoByInstance,
                    WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                        ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false)
                : null;
            return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, replayTodo?.Id));
        }

        if (todo.StatusKey != "active" || todo.Revision != request.ExpectedRevision)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (instance.StatusKey == "suspended")
        {
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        }

        if (instance.StatusKey != "active")
        {
            return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        }

        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
            WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        submission = await branchStates.ReadAsync(todo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
        if (submission is not null && submission.Revision != request.ExpectedFormRevision)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var definition = asset is null
            ? null
            : JsonSerializer.Deserialize(
                asset.CanonicalJson,
                WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var formSchema = asset is null
            ? null
            : JsonSerializer.Deserialize(
                asset.FormSchemaJson,
                WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (!WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(queryExecutor, formVersionId, scope, token).ConfigureAwait(false)))
            return Failure(WorkflowErrorCodes.FormExtensionForbidden, ErrorType.Conflict);
        WorkflowRuntimePlan? runtimePlan = null;
        var transition = default(WorkflowRuntimeTransition);
        var hasPlan = definition is not null && formSchema is not null &&
            WorkflowRuntimePlan.TryCreate(definition, formSchema, out runtimePlan) &&
            runtimePlan!.ContainsHumanNode(todo.NodeKey);
        var patchedSubmission = asset is null || submission is null || !hasPlan
            ? null
            : BuildPatchedSubmission(
                asset.CanonicalJson,
                todo.NodeKey,
                asset.FormSchemaJson,
                submission.SubmissionJson,
                request.FieldPatch);
        if (patchedSubmission is null)
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }

        if (!WorkflowHumanActionPolicy.TryResolve(asset!.CanonicalJson, todo.NodeKey, out var publishedActions))
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var policyCheck = publishedActions!.Validate(actionKey, request.Comment, request.SignatureFileId);
        if (!policyCheck.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(policyCheck.Error!);

        // 人工办理、续办与生命周期动作先锁同一实例，避免反向锁序并保护汇合原子性。
        var instanceLocked = await commandExecutor.ExecuteAsync(
            WorkflowSql.LockInstanceForMultiApproval,
            WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)), token)
            .ConfigureAwait(false);
        if (instanceLocked != 1)
        {
            var concurrentReceipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                    ("IdempotencyKey", request.IdempotencyKey.Trim())), token).ConfigureAwait(false);
            return ReplayApproval(
                todo.Id, instance, formVersionId, actorUserId, actionKey, requestHash, concurrentReceipt);
        }

        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Claim)
        {
            if (actionKey != "complete") return Failure(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
            return await ActClaimAsync(todo, instance, formVersionId, submission!, runtimePlan!, patchedSubmission,
                formSchema!, scope, actorUserId, request, requestHash, preparation, token).ConfigureAwait(false);
        }
        if (todo.NodeTypeKey == WorkflowHumanActivityKinds.Task)
        {
            if (actionKey != "complete") return Failure(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
            return await ActTaskAsync(todo, instance, formVersionId, submission!, runtimePlan!, patchedSubmission,
                formSchema!, scope, actorUserId, request, requestHash, preparation, token).ConfigureAwait(false);
        }
        if (todo.ApprovalModeKey is { } approvalModeKey &&
            !await countersignService.ManagesAsync(todo, scope, token).ConfigureAwait(false))
        {
            return await ActApprovalSlotAsync(
                todo, approvalModeKey, instance, formVersionId, submission!, runtimePlan!, patchedSubmission,
                formSchema!, scope, actorUserId, request, actionKey, requestHash, preparation, token).ConfigureAwait(false);
        }

        if (actionKey == "approve")
        {
            var patchedValues = JsonSerializer.Deserialize(
                patchedSubmission,
                WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (patchedValues is null ||
                !runtimePlan!.TryResolveAfterHuman(
                    todo.NodeKey,
                    patchedValues,
                    todo.ParallelJoinNodeKey,
                    out transition))
            {
                return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            }
        }
        else
        {
            // 驳回直接终止当前实例，不得执行或求值审批节点之后的任何自动节点。
            transition = new WorkflowRuntimeTransition(null, true, []);
        }

        var now = clock.UtcNow;
        var advancesToNextApproval = actionKey == "approve" && !transition.CompletesInstance;
        var instanceStatus = actionKey == "reject"
            ? "rejected"
            : advancesToNextApproval ? "active" : "completed";
        var stepStatus = actionKey == "reject" ? "rejected" : "completed";
        Guid? nextTodoId = null;
        var submissionUpdated = todo.ParallelJoinId is not null
            ? await branchStates.WriteAsync(todo, request.FieldPatch, request.ExpectedFormRevision,
                scope.TenantScopeKey, token).ConfigureAwait(false)
            : await commandExecutor.ExecuteAsync(
            WorkflowSql.UpdateFormSubmissionWithRevision,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", formVersionId),
                ("SubmissionJson", patchedSubmission), ("UpdatedById", actorUserId),
                ("UpdatedAtUtc", now), ("Revision", request.ExpectedFormRevision)), token).ConfigureAwait(false);
        if (submissionUpdated != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var attachmentSync = await SyncSubmissionAttachmentsAsync(
            actorUserId,
            submission!,
            instance.Id,
            scope,
            formSchema!,
            submission!.SubmissionJson,
            patchedSubmission,
            preparation.Attachments,
            token).ConfigureAwait(false);
        if (!attachmentSync.IsSuccess)
        {
            return Result<WorkflowInstanceResponse>.Failure(attachmentSync.Error!);
        }
        var signatureWrite = await signatureCoordinator.WriteAsync(preparation.Signature, todo, instance,
            actorUserId, actionKey, request.IdempotencyKey, request.SignatureFileId, request.ExpectedFormRevision + 1,
            patchedSubmission, token).ConfigureAwait(false);
        if (!signatureWrite.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(signatureWrite.Error!);
        if (await RecordTodoOutputAsync(todo, scope, patchedSubmission, request.ExpectedFormRevision + 1, token).ConfigureAwait(false) != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);

        var countersignResult = await countersignService.TryHandleActAsync(
            todo, instance, formVersionId, patchedSubmission, request, actionKey, actorUserId, scope, preparation.Approvals, token)
            .ConfigureAwait(false);
        if (countersignResult is not null)
        {
            if (countersignResult.IsSuccess)
                await RecordCountersignDecisionAsync(todo, scope, actorUserId, request, actionKey, requestHash,
                    countersignResult.Value!, now, token).ConfigureAwait(false);
            return countersignResult;
        }

        var todoUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteTodoWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                ("ResultActionKey", actionKey), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false);
        var stepUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteStepWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.StepId), ("InstanceId", instance.Id),
                ("StatusKey", stepStatus), ("CompletedAtUtc", now),
                ("Revision", todo.StepRevision)), token).ConfigureAwait(false);
        if (todoUpdated != 1 || stepUpdated != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);

        if (actionKey == "reject")
        {
            await WorkflowHumanActivityLifecycle.CancelAsync(commandExecutor, instance.Id, scope.TenantScopeKey, now, token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelApprovalSlots,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelWork,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Now", now)), token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.CancelAllActiveTodosByInstance,
                WorkflowSqlParameters.Create(
                    ("InstanceId", instance.Id),
                    ("CompletedAtUtc", now),
                    ("ResultActionKey", "cancelled")), token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.CancelAllActiveStepsByInstance,
                WorkflowSqlParameters.Create(
                    ("InstanceId", instance.Id),
                    ("StatusKey", "cancelled"),
                    ("CompletedAtUtc", now)), token).ConfigureAwait(false);
            await parallelJoinCoordinator.CancelWaitingJoinsAsync(instance.Id, now, token)
                .ConfigureAwait(false);
        }

        var nextExecutionSequence = actionKey == "approve"
            ? await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowSql.FindNextStepExecutionSequence,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false)
            : 0;
        if (actionKey == "approve")
        {
            var patchedValues = JsonSerializer.Deserialize(
                patchedSubmission,
                WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement) ?? [];
            var execution = await transitionExecutor.ExecuteAsync(
                instance,
                scope,
                await WorkflowExecutionEpochScope.ReadForStepAsync(queryExecutor, instance.Id, scope.TenantScopeKey, todo.StepId, token).ConfigureAwait(false) ?? Guid.Empty,
                runtimePlan!,
                transition,
                patchedValues,
                nextExecutionSequence,
                now,
                instance.StartedById,
                todo.ParallelJoinId,
                todo.ParallelBranchKey,
                token, preparedAssignees: preparation.Approvals, sourceStepId: todo.StepId).ConfigureAwait(false);
            if (!execution.IsSuccess)
            {
                return Result<WorkflowInstanceResponse>.Failure(execution.Error!);
            }

            nextTodoId = execution.Value!.FirstTodoId;
            instanceStatus = execution.Value.InstanceStatusKey;
        }
        var instanceUpdated = await commandExecutor.ExecuteAsync(instanceStatus switch
            {
                "active" => WorkflowSql.AdvanceInstanceWithRevision,
                "suspended" => WorkflowSql.SuspendInstanceWithRevision,
                _ => WorkflowSql.CompleteInstanceWithRevision,
            },
            WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", instance.Revision), ("StatusKey", instanceStatus), ("CompletedAtUtc", now)), token)
            .ConfigureAwait(false);
        if (instanceUpdated != 1) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);

        await commandExecutor.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TodoId", todo.Id), ("ActionKey", actionKey),
                ("ActorUserId", actorUserId), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", request.IdempotencyKey.Trim()),
                ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now),
                ("ResultStatusKey", instanceStatus), ("ResultTodoId", nextTodoId)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TransitionKey", $"todo.{actionKey}"),
                ("FromStatusKey", "active"), ("ToStatusKey", instanceStatus),
                ("IdempotencyKey", request.IdempotencyKey.Trim()),
                ("Summary", requestHash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instance.Id),
                ("OperationKey", $"todo.{actionKey}"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id),
                ("OutcomeKey", "succeeded"), ("DetailJson", null),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        // 只在首次成功迁移后发布提醒；上方幂等回放分支不会再次追加 Outbox。
        if (instanceStatus == "rejected")
        {
            await notificationPublisher.PublishInstanceRejectedAsync(
                instance.Id,
                instance.StartedById,
                instance.BusinessType,
                instance.BusinessId,
                now,
                token).ConfigureAwait(false);
        }
        else if (instanceStatus == "completed")
        {
            await notificationPublisher.PublishInstanceCompletedAsync(
                instance.Id,
                instance.StartedById,
                instance.BusinessType,
                instance.BusinessId,
                now,
                token).ConfigureAwait(false);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, instanceStatus,
            instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
    }

    /// <summary>提交一人一票决定，并按步骤权威票数原子收敛多人审批。</summary>
    /// <param name="todo">当前审批待办及步骤快照。</param>
    /// <param name="approvalModeKey">调用点已验证为非空的多人审批模式键。</param>
    /// <param name="instance">当前活动实例。</param>
    /// <param name="formVersionId">实例绑定的表单版本。</param>
    /// <param name="submission">当前表单提交快照。</param>
    /// <param name="runtimePlan">发布定义生成的运行计划。</param>
    /// <param name="patchedSubmission">应用当前字段补丁后的提交 JSON。</param>
    /// <param name="scope">可信租户作用域。</param>
    /// <param name="actorUserId">当前办理人。</param>
    /// <param name="request">审批动作请求。</param>
    /// <param name="actionKey">批准或驳回机器键。</param>
    /// <param name="requestHash">用于幂等校验的请求摘要。</param>
    /// <param name="token">取消当前事务操作的令牌。</param>
    /// <returns>投票后的实例快照或稳定业务错误。</returns>
    private async Task<Result<WorkflowInstanceResponse>> ActApprovalSlotAsync(
        WorkflowTodoRuntimeRecord todo,
        string approvalModeKey,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        WorkflowFormSubmissionRecord submission,
        WorkflowRuntimePlan runtimePlan,
        string patchedSubmission,
        WorkflowFormSchema formSchema,
        WorkflowManagementScope scope,
        Guid actorUserId,
        ActWorkflowTodoRequest request,
        string actionKey,
        string requestHash,
        WorkflowActionPreparation preparation,
        CancellationToken token)
    {
        if (todo.RequiredApprovalCount is not { } requiredApprovalCount ||
            todo.ApprovalSlotCount is not { } approvalSlotCount ||
            requiredApprovalCount < 1 || approvalSlotCount is < 1 or > 20 || approvalModeKey != "group" && approvalSlotCount < requiredApprovalCount)
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }

        var slot = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(
            WorkflowSql.FindApprovalSlotByTodo,
            WorkflowSqlParameters.Create(("TodoId", todo.Id), ("AssigneeUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (slot is null)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var now = clock.UtcNow;
        var submissionUpdated = todo.ParallelJoinId is not null
            ? await branchStates.WriteAsync(todo, request.FieldPatch, request.ExpectedFormRevision,
                scope.TenantScopeKey, token).ConfigureAwait(false)
            : await commandExecutor.ExecuteAsync(
            WorkflowSql.UpdateFormSubmissionWithRevision,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", formVersionId),
                ("SubmissionJson", patchedSubmission), ("UpdatedById", actorUserId),
                ("UpdatedAtUtc", now), ("Revision", request.ExpectedFormRevision)), token).ConfigureAwait(false);
        if (submissionUpdated != 1)
        {
            var concurrentReceipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                    ("IdempotencyKey", request.IdempotencyKey.Trim())), token).ConfigureAwait(false);
            return ReplayApproval(todo.Id, instance, formVersionId, actorUserId, actionKey, requestHash, concurrentReceipt);
        }
        var todoUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteTodoWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                ("ResultActionKey", actionKey), ("Revision", request.ExpectedRevision)), token)
            .ConfigureAwait(false);
        var slotUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.DecideApprovalSlotWithRevision,
            WorkflowSqlParameters.Create(("Id", slot.Id), ("StepId", todo.StepId),
                ("TodoId", todo.Id), ("AssigneeUserId", actorUserId),
                ("DecisionKey", actionKey), ("DecidedAtUtc", now), ("Revision", slot.Revision)), token)
            .ConfigureAwait(false);
        if (todoUpdated != 1 || slotUpdated != 1)
        {
            // 表单 CAS 已成功，后续竞争失败只能回滚，不能以成功重放提交本事务的部分写入。
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var attachmentSync = await SyncSubmissionAttachmentsAsync(
            actorUserId,
            submission,
            instance.Id,
            scope,
            formSchema,
            submission.SubmissionJson,
            patchedSubmission,
            preparation.Attachments,
            token).ConfigureAwait(false);
        if (!attachmentSync.IsSuccess)
        {
            return Result<WorkflowInstanceResponse>.Failure(attachmentSync.Error!);
        }
        var signatureWrite = await signatureCoordinator.WriteAsync(preparation.Signature, todo, instance,
            actorUserId, actionKey, request.IdempotencyKey, request.SignatureFileId, request.ExpectedFormRevision + 1,
            patchedSubmission, token).ConfigureAwait(false);
        if (!signatureWrite.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(signatureWrite.Error!);
        if (await RecordTodoOutputAsync(todo, scope, patchedSubmission, request.ExpectedFormRevision + 1, token).ConfigureAwait(false) != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);

        var approvalValues = JsonSerializer.Deserialize(patchedSubmission,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (approvalValues is null) return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var resolution = await stepResolution.ResolveAsync(todo, instance, scope, runtimePlan, approvalValues,
              preparation.Approvals, now, token, actorUserId).ConfigureAwait(false);
        if (!resolution.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(resolution.Error!);
        var tally = resolution.Value!.Tally;
        var nextTodoId = resolution.Value.NextTodoId;
        var instanceStatus = resolution.Value.InstanceStatusKey;

        var instanceUpdated = await commandExecutor.ExecuteAsync(instanceStatus switch
            {
                "active" => WorkflowSql.AdvanceInstanceWithRevision,
                "suspended" => WorkflowSql.SuspendInstanceWithRevision,
                _ => WorkflowSql.CompleteInstanceWithRevision,
            },
            WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", instance.Revision), ("StatusKey", instanceStatus), ("CompletedAtUtc", now)), token)
            .ConfigureAwait(false);
        if (instanceUpdated != 1) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        if (instanceStatus == "rejected")
        {
            await WorkflowHumanActivityLifecycle.CancelAsync(commandExecutor, instance.Id, scope.TenantScopeKey, now, token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelApprovalSlots,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(WorkflowSql.CancelAllActiveTodosByInstance,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("CompletedAtUtc", now),
                    ("ResultActionKey", "cancelled")), token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(WorkflowSql.CancelAllActiveStepsByInstance,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("CompletedAtUtc", now),
                    ("StatusKey", "cancelled")), token).ConfigureAwait(false);
            await parallelJoinCoordinator.CancelWaitingJoinsAsync(instance.Id, now, token).ConfigureAwait(false);
        }
        if (instanceStatus is "rejected" or "completed")
            await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelWork,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Now", now)), token).ConfigureAwait(false);

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertApprovalActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TodoId", todo.Id), ("ActionKey", actionKey),
                ("ActorUserId", actorUserId), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", request.IdempotencyKey.Trim()),
                ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now),
                ("ResultStatusKey", instanceStatus), ("ResultTodoId", nextTodoId)), token)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TransitionKey", $"todo.{actionKey}"),
                ("FromStatusKey", "active"), ("ToStatusKey", instanceStatus),
                ("IdempotencyKey", request.IdempotencyKey.Trim()),
                ("Summary", requestHash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instance.Id),
                ("OperationKey", $"todo.{actionKey}"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id),
                ("OutcomeKey", "succeeded"),
                ("DetailJson", JsonSerializer.Serialize(
                    new WorkflowApprovalAuditDetail(
                        approvalModeKey,
                        requiredApprovalCount,
                        (int)tally.ApprovedCount,
                        (int)tally.RejectedCount,
                        (int)tally.PendingCount),
                    WorkflowJsonSerializerContext.Default.WorkflowApprovalAuditDetail)),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        if (instanceStatus == "rejected")
        {
            await notificationPublisher.PublishInstanceRejectedAsync(
                instance.Id, instance.StartedById, instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
        }
        else if (instanceStatus == "completed")
        {
            await notificationPublisher.PublishInstanceCompletedAsync(
                instance.Id, instance.StartedById, instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, instanceStatus,
            instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
    }

    /// <summary>在 Workflow 本地事务中关闭来源工作并重新激活目标人工审批节点。</summary>
    /// <param name="todoId">当前活动待办标识。</param>
    /// <param name="actorUserId">当前认证用户标识。</param>
    /// <param name="request">已经由端点绑定的退回请求。</param>
    /// <param name="token">取消当前事务操作的令牌。</param>
    /// <returns>退回后的实例快照或稳定业务错误。</returns>
    private async Task<Result<WorkflowInstanceResponse>> ReturnCoreAsync(
        Guid todoId,
        Guid actorUserId,
        ReturnWorkflowTodoRequest request,
        WorkflowActionPreparation preparation,
        CancellationToken token)
    {
        var comment = request.Comment?.Trim();
        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (request.TargetStepId == Guid.Empty || request.ExpectedRevision < 1 ||
            request.FieldPatch.ValueKind != JsonValueKind.Object ||
            idempotencyKey is not { Length: >= 1 and <= 128 } ||
            comment is not { Length: >= 1 and <= 512 })
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
            WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)),
            token).ConfigureAwait(false);
        if (todo is null)
        {
            return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        if (todo.AssigneeUserId != actorUserId)
        {
            return Failure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var requestHash = HashRequest(request);
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("IdempotencyKey", idempotencyKey)), token).ConfigureAwait(false);
        if (receipt is not null)
        {
            return ReplayReturn(todo.Id, instance, formVersionId, actorUserId, requestHash, receipt);
        }

        if (todo.StatusKey != "active" || todo.Revision != request.ExpectedRevision)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (instance.StatusKey == "suspended")
        {
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        }

        if (instance.StatusKey != "active")
        {
            return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        }

        // ExecutePreparedAsync已取得可信根锁；在该锁内重读真实步骤、分叉、工作及有效绑定。
        var scopeState = await new WorkflowReturnScopeReader(queryExecutor, databaseOptions).ReadAsync(instance, todo.StepId, token).ConfigureAwait(false);
        var ancestors = scopeState?.Bindings.Select(binding => binding.StepId).Distinct().ToArray() ?? [];
        // 服务端按实例、类型和当前有效状态复核目标，绝不信任页面曾经加载的候选列表。
        var target = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoReturnTargetRecord>(
            WorkflowSql.FindTodoReturnTarget,
            WorkflowSqlParameters.Create(("TargetStepId", request.TargetStepId),
                ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("CurrentStepId", todo.StepId),("AncestorStepIds",ancestors.Length==0?new[]{Guid.Empty}:ancestors)),
            token).ConfigureAwait(false);
        if (target is null || !ancestors.Contains(target.StepId))
        {
            // 并发退回时，另一请求可能已先完成并失效目标；同幂等键应回放收据而非返回无效目标。
            var concurrentReceipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                    ("IdempotencyKey", idempotencyKey)), token).ConfigureAwait(false);
            return concurrentReceipt is null
                ? Failure(WorkflowErrorCodes.TodoReturnTargetInvalid, ErrorType.BusinessRule)
                : ReplayReturn(todo.Id, instance, formVersionId, actorUserId, requestHash, concurrentReceipt);
        }

        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
            WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var mainSubmission = submission;
        submission = await branchStates.ReadAsync(todo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
        if (request.ExpectedFormRevision < 1 ||
            (submission is not null && submission.Revision != request.ExpectedFormRevision))
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var definition = asset is null ? null : JsonSerializer.Deserialize(
            asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var formSchema = asset is null ? null : JsonSerializer.Deserialize(
            asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (!WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(queryExecutor, formVersionId, scope, token).ConfigureAwait(false)))
            return Failure(WorkflowErrorCodes.FormExtensionForbidden, ErrorType.Conflict);
        WorkflowRuntimePlan? runtimePlan = null;
        var hasPlan = definition is not null && formSchema is not null &&
            WorkflowRuntimePlan.TryCreate(definition, formSchema, out runtimePlan) &&
            runtimePlan!.ContainsHumanNode(todo.NodeKey) &&
            runtimePlan.ContainsHumanNode(target.NodeKey);
        var patchedSubmission = asset is null || submission is null || !hasPlan
            ? null
            : BuildPatchedSubmission(asset.CanonicalJson, todo.NodeKey, asset.FormSchemaJson,
                submission.SubmissionJson, request.FieldPatch);
        if (patchedSubmission is null)
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }

        if (scopeState is null || !scopeState.TrySelect(instance, runtimePlan!, todo.StepId, target.StepId, out var selected))
            return Failure(WorkflowErrorCodes.TodoReturnTargetInvalid, ErrorType.BusinessRule);
        var returnInput = await new WorkflowReturnInputCoordinator(queryExecutor, branchStates).PrepareAsync(instance, todo,
            scopeState, selected!, mainSubmission!, submission!, patchedSubmission, token).ConfigureAwait(false);
        if (returnInput is null) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);

        // 原完成尝试的人员与分组才是退回依据，不能让角色名册变化悄悄改写重开的席位。
        var targetSnapshot = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowAssigneeSnapshotRecord>(WorkflowReturnRoundSql.FindCompletedSnapshot,
            WorkflowSqlParameters.Create(("StepId", target.StepId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (targetSnapshot is null || !runtimePlan!.TryGetReturnNode(target.NodeKey, out var targetNode) ||
            !WorkflowReturnAssigneeSnapshot.TryRestore(targetNode!.NodeTypeKey, targetNode.Config, instance.StartedById,
                targetSnapshot.AssigneeSnapshotJson, targetSnapshot.AssigneeSnapshotHash, out _))
            return Failure(WorkflowErrorCodes.TodoReturnTargetInvalid, ErrorType.BusinessRule);

        var now = clock.UtcNow;
        // 来源表单与可选外层投影都在同一根锁内CAS，签名仍绑定来源视图。
        if (!WorkflowHumanActionPolicy.TryResolve(asset!.CanonicalJson, todo.NodeKey, out var publishedActions))
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var policyCheck = publishedActions!.Validate("return", request.Comment, request.SignatureFileId);
        if (!policyCheck.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(policyCheck.Error!);

        var submissionUpdated = todo.ParallelJoinId is not null
            ? await branchStates.WriteAsync(todo, request.FieldPatch, request.ExpectedFormRevision, scope.TenantScopeKey, token).ConfigureAwait(false)
            : await commandExecutor.ExecuteAsync(
            WorkflowSql.UpdateFormSubmissionWithRevision,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", formVersionId),
                ("SubmissionJson", patchedSubmission), ("UpdatedById", actorUserId),
                ("UpdatedAtUtc", now), ("Revision", request.ExpectedFormRevision)), token).ConfigureAwait(false);
        if (submissionUpdated != 1)
        {
            var concurrentReceipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                    ("IdempotencyKey", idempotencyKey)), token).ConfigureAwait(false);
            return concurrentReceipt is null
                ? Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict)
                : ReplayReturn(todo.Id, instance, formVersionId, actorUserId, requestHash, concurrentReceipt);
        }

        if (returnInput.Parent is { } parentSubmission)
        {
            var parentUpdated = selected!.ParallelJoinId is not null
                ? await branchStates.WriteAsync(instance.Id, selected.ParallelJoinId, selected.ParallelBranchKey, returnInput.Delta,
                    parentSubmission.Revision, scope.TenantScopeKey, token).ConfigureAwait(false)
                : await commandExecutor.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
                    WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", formVersionId), ("SubmissionJson", returnInput.Json),
                        ("UpdatedById", actorUserId), ("UpdatedAtUtc", now), ("Revision", parentSubmission.Revision)), token).ConfigureAwait(false);
            if (parentUpdated != 1) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var attachmentSync = await SyncSubmissionAttachmentsAsync(
            actorUserId,
            submission!,
            instance.Id,
            scope,
            formSchema!,
            submission!.SubmissionJson,
            patchedSubmission,
            preparation.Attachments,
            token).ConfigureAwait(false);
        if (!attachmentSync.IsSuccess)
        {
            return Result<WorkflowInstanceResponse>.Failure(attachmentSync.Error!);
        }
        var signatureWrite = await signatureCoordinator.WriteAsync(preparation.Signature, todo, instance,
            actorUserId, "return", idempotencyKey, request.SignatureFileId, request.ExpectedFormRevision + 1,
            patchedSubmission, token).ConfigureAwait(false);
        if (!signatureWrite.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(signatureWrite.Error!);
        if (await RecordTodoOutputAsync(todo, scope, patchedSubmission, request.ExpectedFormRevision + 1, token).ConfigureAwait(false) != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);

        var todoUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteTodoWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                ("ResultActionKey", "return"), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false);
        var stepUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.ReturnStepWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.StepId), ("InstanceId", instance.Id),
                ("CompletedAtUtc", now), ("Revision", todo.StepRevision)), token).ConfigureAwait(false);
        var instanceUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.AdvanceInstanceWithRevision,
            WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)),
            token).ConfigureAwait(false);
        if (todoUpdated != 1 || stepUpdated != 1 || instanceUpdated != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        await WorkflowReturnClosure.ApplyAsync(commandExecutor, instance, selected!, now, token).ConfigureAwait(false);

        var nextExecutionSequence = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            WorkflowSql.FindNextStepExecutionSequence,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);

        // 新代次和原目标摘要与唯一入队收据同事务保存，Worker只复核该快照成员当前资格。
        var returnValues = JsonSerializer.Deserialize(returnInput.Json,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)!;
        var returnRound = await new WorkflowReturnRoundCoordinator(queryExecutor, commandExecutor, idGenerator).EnterAsync(instance,
            runtimePlan!, todo.StepId, target.StepId, target.NodeKey, targetSnapshot.AssigneeSnapshotHash, returnValues,
            returnInput.Revision, nextExecutionSequence, now, token, selected).ConfigureAwait(false);
        if (!returnRound.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(returnRound.Error!);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertApprovalActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TodoId", todo.Id), ("ActionKey", "return"),
                ("ActorUserId", actorUserId), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", idempotencyKey), ("CommentSummary", comment),
                ("CreatedAtUtc", now), ("ResultStatusKey", "active"), ("ResultTodoId", null)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TransitionKey", "todo.return"),
                ("FromStatusKey", "active"), ("ToStatusKey", "active"),
                ("IdempotencyKey", idempotencyKey), ("Summary", requestHash),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", null), ("TransitionKey", "activity.return.enqueued"),
                ("FromStatusKey", "completed"), ("ToStatusKey", "active"),
                ("IdempotencyKey", null), ("Summary", target.NodeKey),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        var auditDetail = JsonSerializer.Serialize(
            new WorkflowTodoReturnAuditDetail(todo.StepId, target.StepId, target.NodeKey),
            WorkflowJsonSerializerContext.Default.WorkflowTodoReturnAuditDetail);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instance.Id),
                ("OperationKey", "todo.return"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id),
                ("OutcomeKey", "succeeded"), ("DetailJson", auditDetail),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
            instance.Revision + 1, null, instance.StartedAtUtc));
    }

    /// <summary>按首次成功投票的持久化结果构造并发幂等响应。</summary>
    /// <param name="todoId">本次办理的原待办身份，不能重放另一待办的回执。</param>
    /// <param name="instance">当前读取到的实例，用于提供不可变业务标识。</param>
    /// <param name="formVersionId">实例固化的表单版本标识。</param>
    /// <param name="actorUserId">当前操作人标识。</param>
    /// <param name="actionKey">当前批准或驳回动作键。</param>
    /// <param name="requestHash">当前请求的规范哈希。</param>
    /// <param name="receipt">竞争事务可能已经提交的动作回执。</param>
    /// <returns>原始成功响应；未找到回执或语义不一致时返回冲突。</returns>
    private static Result<WorkflowInstanceResponse> ReplayApproval(
        Guid todoId,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid actorUserId,
        string actionKey,
        string requestHash,
        WorkflowActionReceiptRecord? receipt)
    {
        if (receipt is null || receipt.TodoId != todoId || receipt.ActionKey != actionKey ||
            receipt.ActorUserId != actorUserId || receipt.RequestHash != requestHash ||
            receipt.ResultStatusKey is null)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, receipt.ResultStatusKey,
            receipt.InstanceRevision, receipt.ResultTodoId, instance.StartedAtUtc));
    }

    /// <summary>按首次成功动作的持久化回执构造稳定退回响应，不读取随后变化的实例状态。</summary>
    /// <param name="todoId">本次办理的原待办身份，不能重放另一待办的回执。</param>
    /// <param name="instance">当前读取到的实例，用于提供不可变业务标识。</param>
    /// <param name="formVersionId">实例固化的表单版本标识。</param>
    /// <param name="actorUserId">当前操作人标识。</param>
    /// <param name="requestHash">当前请求的规范哈希。</param>
    /// <param name="receipt">首次成功动作的持久化回执。</param>
    /// <returns>原始成功响应；耐久重新激活的首次回执允许没有即时待办。</returns>
    private static Result<WorkflowInstanceResponse> ReplayReturn(
        Guid todoId,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid actorUserId,
        string requestHash,
        WorkflowActionReceiptRecord receipt)
    {
        if (receipt.TodoId != todoId || receipt.ActionKey != "return" || receipt.ActorUserId != actorUserId ||
            receipt.RequestHash != requestHash || receipt.ResultStatusKey != "active")
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
            receipt.InstanceRevision, receipt.ResultTodoId, instance.StartedAtUtc));
    }

    private static string? BuildPatchedSubmission(
        string canonicalDefinitionJson,
        string nodeKey,
        string formSchemaJson,
        string submissionJson,
        JsonElement patch)
    {
        var schema = JsonSerializer.Deserialize(
            formSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = JsonSerializer.Deserialize(
            submissionJson,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (schema is null || values is null)
        {
            return null;
        }

        if (!WorkflowNodeFieldPolicy.TryResolve(
                canonicalDefinitionJson, nodeKey, schema, out var policy) ||
            !policy!.TryApplyPatch(schema, values, patch, out var patched))
        {
            return null;
        }

        return JsonSerializer.Serialize(
            patched,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
    }

    private static string HashRequest(string actionKey, ActWorkflowTodoRequest request)
    {
        var value = $"{actionKey}\n{request.ExpectedRevision}\n{request.ExpectedFormRevision}\n{request.FieldPatch.GetRawText()}\n{request.Comment?.Trim()}\nsignature:{request.SignatureFileId:D}";
        return HashUtf8(value);
    }

    // 加签接管仍属于同一次办理；回执、轨迹和领域审计与链推进同事务提交，不能只返回临时结果。
    private async Task RecordCountersignDecisionAsync(WorkflowTodoRuntimeRecord todo, WorkflowManagementScope scope,
        Guid actorId, ActWorkflowTodoRequest request, string actionKey, string requestHash,
        WorkflowInstanceResponse result, DateTimeOffset now, CancellationToken token)
    {
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", todo.InstanceId),
                ("StepId", todo.StepId), ("TodoId", todo.Id), ("ActionKey", actionKey), ("ActorUserId", actorId),
                ("InstanceRevision", result.Revision), ("IdempotencyKey", request.IdempotencyKey.Trim()),
                ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now),
                ("ResultStatusKey", result.StatusKey), ("ResultTodoId", result.ActiveTodoId)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("Id", idGenerator.NewId()), ("InstanceId", todo.InstanceId), ("StepId", todo.StepId),
                ("TransitionKey", $"todo.{actionKey}"), ("FromStatusKey", "active"), ("ToStatusKey", result.StatusKey),
                ("IdempotencyKey", request.IdempotencyKey.Trim()), ("Summary", requestHash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                ("InstanceId", todo.InstanceId), ("OperationKey", $"todo.{actionKey}"), ("ActorUserId", actorId),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id), ("OutcomeKey", "succeeded"),
                ("DetailJson", null), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
    }

    private Task<int> RecordTodoOutputAsync(WorkflowTodoRuntimeRecord todo, WorkflowManagementScope scope,
        string json, long revision, CancellationToken token) => commandExecutor.ExecuteAsync(WorkflowTodoViewSql.SetOutput,
            WorkflowSqlParameters.Create(("TodoId", todo.Id), ("InstanceId", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey),
                ("OutputJson", json), ("OutputRevision", revision)), token);

    private async Task<Result<bool>> SyncSubmissionAttachmentsAsync(
        Guid actorUserId,
        WorkflowFormSubmissionRecord submission,
        Guid instanceId,
        WorkflowManagementScope scope,
        WorkflowFormSchema schema,
        string previousSubmissionJson,
        string nextSubmissionJson,
        WorkflowAttachmentPlan? prepared,
        CancellationToken cancellationToken)
    {
        var previousValues = JsonSerializer.Deserialize(
            previousSubmissionJson,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var nextValues = JsonSerializer.Deserialize(
            nextSubmissionJson,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (nextValues is null)
        {
            return Result<bool>.Failure(new Error(
                WorkflowErrorCodes.SchemaInvalid,
                "The workflow submission payload is invalid.",
                ErrorType.Validation));
        }

        return await attachmentCoordinator.WritePreparedAsync(
            prepared,
            actorUserId,
            submission,
            instanceId,
            scope.TenantScopeKey,
            schema,
            previousValues,
            nextValues,
            nextSubmissionJson,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>计算包含目标步骤的稳定退回请求摘要。</summary>
    /// <param name="request">退回请求。</param>
    /// <returns>小写十六进制 SHA-256 摘要。</returns>
    private static string HashRequest(ReturnWorkflowTodoRequest request)
    {
        var value = $"return\n{request.TargetStepId:D}\n{request.ExpectedRevision}\n{request.ExpectedFormRevision}\n" +
            $"{request.FieldPatch.GetRawText()}\n{request.Comment.Trim()}\nsignature:{request.SignatureFileId:D}";
        return HashUtf8(value);
    }

    private static string HashUtf8(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private SqlStatement ApprovalTallyStatement =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowSql.FindApprovalTallyByStepSqlServer,
            DatabaseProvider.MySql => WorkflowSql.FindApprovalTallyByStepMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };

    private SqlStatement GroupTallyStatement => databaseOptions.Value.Provider switch
    {
        DatabaseProvider.SqlServer => WorkflowGroupApprovalSql.TallyGroupsSqlServer,
        DatabaseProvider.MySql => WorkflowGroupApprovalSql.TallyGroupsMySql,
        _ => throw new InvalidOperationException("Unsupported workflow database provider."),
    };

    private static WorkflowInstanceResponse Map(
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid? activeTodoId) =>
        new(instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, instance.StatusKey,
            instance.Revision, activeTodoId, instance.StartedAtUtc);

    private static Result<WorkflowInstanceResponse> Failure(string code, ErrorType type) =>
        Result<WorkflowInstanceResponse>.Failure(
            new Error(code, "The workflow todo operation failed.", type));

    private static Result<WorkflowTodoDetailResponse> DetailFailure(string code, ErrorType type) =>
        Result<WorkflowTodoDetailResponse>.Failure(
            new Error(code, "The workflow todo detail operation failed.", type));

    /// <summary>构造合法退回目标查询的稳定错误结果。</summary>
    /// <param name="code">稳定错误码。</param>
    /// <param name="type">错误分类。</param>
    /// <returns>失败结果。</returns>
    private static Result<IReadOnlyList<WorkflowTodoReturnTargetResponse>> ReturnTargetsFailure(
        string code,
        ErrorType type) =>
        Result<IReadOnlyList<WorkflowTodoReturnTargetResponse>>.Failure(
            new Error(code, "The workflow todo return target query failed.", type));
}

/// <summary>同一次动作在事务外准备的办理人与签名快照，不跨请求复用。</summary>
internal sealed record WorkflowActionPreparation(WorkflowPreparedHumanActivities Approvals, WorkflowPreparedSignature? Signature, WorkflowAttachmentPlan? Attachments)
{
    public Guid? InstanceId { get; init; }
    public static WorkflowActionPreparation Empty { get; } = new(WorkflowPreparedHumanActivities.Empty, null, null);
}
