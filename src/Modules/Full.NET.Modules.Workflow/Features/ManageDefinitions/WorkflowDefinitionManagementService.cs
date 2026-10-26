using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Execution.Connectors;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>维护流程定义草稿，并在可信作用域内绑定不可变表单版本后发布。</summary>
/// <param name="queryExecutor">受作用域约束的查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="transaction">工作流本地事务边界。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="hostUserDirectory">Identity 提供的活动 Host 用户批量候选目录。</param>
/// <param name="tenantUserDirectory">Identity 提供的当前 Tenant 活动用户批量候选目录。</param>
internal sealed class WorkflowDefinitionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IHostUserBatchSelectionDirectory hostUserDirectory,
    ITenantMemberSelectionDirectory tenantUserDirectory,
    WorkflowAssigneePublishValidator assigneePublishValidator,
    ITenantFeatureEntitlementPort featureEntitlements, WorkflowConnectorCatalog connectors, WorkflowBusinessActionCatalog businessActions)
{
    public async Task<Result<IReadOnlyList<WorkflowDefinitionResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var definitions = await queryExecutor.QueryAsync<WorkflowDefinitionRecord>(
                WorkflowSql.ListDefinitions,
                Parameters(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var drafts = await queryExecutor.QueryAsync<WorkflowDefinitionDraftRecord>(
                WorkflowSql.ListDefinitionDrafts,
                Parameters(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var draftByDefinition = drafts.ToDictionary(item => item.DefinitionId);
        var responses = new List<WorkflowDefinitionResponse>(definitions.Count);
        foreach (var definition in definitions)
        {
            var response = draftByDefinition.TryGetValue(definition.Id, out var draft)
                ? Map(definition, draft)
                : null;
            if (response is not null)
            {
                responses.Add(response);
            }
        }

        return Result<IReadOnlyList<WorkflowDefinitionResponse>>.Success(responses);
    }

    public async Task<Result<WorkflowDefinitionResponse>> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var definition = await FindDefinitionAsync(id, scope, cancellationToken).ConfigureAwait(false);
        var draft = definition is null
            ? null
            : await FindDraftAsync(id, scope, cancellationToken).ConfigureAwait(false);
        var response = definition is null || draft is null ? null : Map(definition, draft);
        return response is null ? NotFound<WorkflowDefinitionResponse>() : Result<WorkflowDefinitionResponse>.Success(response);
    }

    public async Task<Result<IReadOnlyList<WorkflowDefinitionVersionResponse>>> ListVersionsAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await FindDefinitionAsync(definitionId, scope, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound<IReadOnlyList<WorkflowDefinitionVersionResponse>>();
        }

        var rows = await queryExecutor.QueryAsync<WorkflowDefinitionVersionRecord>(
                WorkflowSql.ListDefinitionVersions,
                Parameters(("DefinitionId", definitionId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<WorkflowDefinitionVersionResponse>>.Success(rows.Select(Map).ToArray());
    }

    public async Task<Result<WorkflowDefinitionVersionResponse>> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowDefinitionVersionRecord>(
                WorkflowSql.FindDefinitionVersionById,
                Parameters(("Id", versionId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? NotFound<WorkflowDefinitionVersionResponse>() : Result<WorkflowDefinitionVersionResponse>.Success(Map(row));
    }

    public async Task<Result<WorkflowDefinitionResponse>> CreateAsync(
        Guid actorUserId,
        CreateWorkflowDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        var definitionKey = NormalizeKey(request.DefinitionKey);
        if (definitionKey is null || !WorkflowBusinessTitleRules.IsValidTemplate(request.BusinessTitleTemplate))
        {
            return Invalid<WorkflowDefinitionResponse>();
        }

        var template = WorkflowBusinessTitleRules.NormalizeTemplate(request.BusinessTitleTemplate);

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowDefinitionResponse>(scope, cancellationToken)
                .ConfigureAwait(false) is { } createDenied)
        {
            return createDenied;
        }

        var definitionId = idGenerator.NewId();
        var draftId = idGenerator.NewId();
        var now = clock.UtcNow;
        var draftJson = Serialize(request.Draft);
        try
        {
            return await transaction.ExecuteResultAsync(async token =>
            {
                await commandExecutor.ExecuteAsync(
                    WorkflowSql.InsertDefinition,
                    Parameters(("Id", definitionId), ("TenantId", scope.TenantId),
                        ("ScopeKey", scope.ScopeKey), ("TenantScopeKey", scope.TenantScopeKey),
                        ("DefinitionKey", definitionKey), ("DraftId", draftId),
                        ("BusinessTitleTemplate", template),
                        ("CreatedById", actorUserId), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(
                    WorkflowSql.InsertDefinitionDraft,
                    Parameters(("Id", draftId), ("DefinitionId", definitionId),
                        ("DraftJson", draftJson), ("ContentHash", Hash(draftJson)),
                        ("UpdatedById", actorUserId), ("UpdatedAtUtc", now)), token).ConfigureAwait(false);
                return Result<WorkflowDefinitionResponse>.Success(new(
                    definitionId, definitionKey, request.Draft, 1, null, template,
                    WorkflowDefinitionStatusKeys.Active, 1, now, null));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Conflict<WorkflowDefinitionResponse>(WorkflowErrorCodes.DefinitionKeyExists);
        }
    }

    public async Task<Result<WorkflowDefinitionResponse>> UpdateDraftAsync(
        Guid id, Guid actorUserId, UpdateWorkflowDefinitionDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ExpectedRevision < 1 || !WorkflowBusinessTitleRules.IsValidTemplate(request.BusinessTitleTemplate))
        {
            return Invalid<WorkflowDefinitionResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        // Tenancy 权益读取须先完成，避免复用 Workflow 的本地事务。
        if (await TryDenyTenantMutationAsync<WorkflowDefinitionResponse>(scope, cancellationToken).ConfigureAwait(false)
            is { } draftDenied)
        {
            return draftDenied;
        }

        return await transaction.ExecuteResultAsync(
            token => UpdateDraftCoreAsync(id, actorUserId, request, scope, token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>校验不可变定义资产和跨模块用户引用后，在 Workflow 本地事务内发布版本。</summary>
    /// <param name="id">工作流定义标识。</param>
    /// <param name="actorUserId">可信当前操作人标识。</param>
    /// <param name="request">发布修订号与绑定表单版本。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>发布成功后的不可变定义版本，或稳定业务错误。</returns>
    public async Task<Result<WorkflowDefinitionVersionResponse>> PublishAsync(
        Guid id, Guid actorUserId, PublishWorkflowDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        try { return await PublishCoreAsync(id, actorUserId, request, cancellationToken).ConfigureAwait(false); }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.Deadlock)
        {
            // 仅在读取失败或所属事务已回滚之后返回冲突，不在半个发布事务中重试单条SQL。
            return RevisionConflict<WorkflowDefinitionVersionResponse>();
        }
    }

    /// <summary>在乐观并发保护下变更定义启停或归档状态。</summary>
    /// <param name="id">工作流定义标识。</param>
    /// <param name="request">目标状态与期望版本。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>更新后的定义投影，或稳定业务错误。</returns>
    public async Task<Result<WorkflowDefinitionResponse>> SetStatusAsync(
        Guid id,
        SetWorkflowDefinitionStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!WorkflowDefinitionLifecycleRules.IsKnownStatusKey(request.StatusKey) || request.ExpectedVersion < 1)
        {
            return Invalid<WorkflowDefinitionResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowDefinitionResponse>(scope, cancellationToken)
                .ConfigureAwait(false) is { } statusDenied)
        {
            return statusDenied;
        }

        var definition = await FindDefinitionAsync(id, scope, cancellationToken).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowDefinitionResponse>();
        }

        if (definition.StatusKey == WorkflowDefinitionStatusKeys.Archived)
        {
            return Failure<WorkflowDefinitionResponse>(WorkflowErrorCodes.DefinitionArchived, ErrorType.Conflict);
        }

        if (!WorkflowDefinitionLifecycleRules.CanTransition(definition.StatusKey, request.StatusKey))
        {
            return Failure<WorkflowDefinitionResponse>(WorkflowErrorCodes.DefinitionStatusInvalid, ErrorType.Validation);
        }

        if (definition.Version != request.ExpectedVersion)
        {
            return RevisionConflict<WorkflowDefinitionResponse>();
        }

        var affected = await commandExecutor.ExecuteAsync(
            WorkflowSql.UpdateDefinitionStatus,
            Parameters(("Id", id), ("TenantScopeKey", scope.TenantScopeKey),
                ("StatusKey", request.StatusKey), ("UpdatedAtUtc", clock.UtcNow),
                ("ExpectedVersion", request.ExpectedVersion)),
            cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return RevisionConflict<WorkflowDefinitionResponse>();
        }

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>删除未被发布依赖、父调用或任何根历史引用的不可变定义版本。</summary>
    /// <param name="versionId">待删除定义版本标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public async Task<Result<bool>> DeleteVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        try { return await DeleteVersionCoreAsync(versionId, cancellationToken).ConfigureAwait(false); }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.Deadlock)
        {
            return RevisionConflict<bool>();
        }
    }

    private async Task<Result<bool>> DeleteVersionCoreAsync(Guid versionId, CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
        {
            return Failure<bool>(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<bool>(scope, cancellationToken).ConfigureAwait(false) is { } deleteDenied)
        {
            return deleteDenied;
        }

        var version = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowDefinitionVersionRecord>(
                WorkflowSql.FindDefinitionVersionById,
                Parameters(("Id", versionId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return Failure<bool>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var definition = await FindDefinitionAsync(version.DefinitionId, scope, cancellationToken).ConfigureAwait(false);
        if (definition is null)
        {
            return Failure<bool>(WorkflowErrorCodes.DefinitionNotFound, ErrorType.NotFound);
        }

        return await transaction.ExecuteResultAsync(async token =>
        {
            await commandExecutor.ExecuteAsync(WorkflowVersionReferenceSql.LockDefinition,
                Parameters(("DefinitionId", version.DefinitionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            var currentDefinition = await FindDefinitionAsync(version.DefinitionId, scope, token).ConfigureAwait(false);
            var currentVersion = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowDefinitionVersionRecord>(WorkflowSql.FindDefinitionVersionById,
                Parameters(("Id", versionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (currentDefinition is null || currentVersion is null)
                return Failure<bool>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
            if (await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowVersionReferenceSql.HasReferences,
                    Parameters(("DefinitionVersionId", versionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 0)
                return Failure<bool>(WorkflowErrorCodes.VersionInUse, ErrorType.Conflict);
            var remainingVersions = await queryExecutor.QueryAsync<WorkflowDefinitionVersionRecord>(WorkflowSql.ListDefinitionVersions,
                Parameters(("DefinitionId", version.DefinitionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            var nextLatestVersionId = remainingVersions.Where(item => item.Id != versionId).MaxBy(item => item.VersionNumber)?.Id;
            try
            {
                await commandExecutor.ExecuteAsync(WorkflowVersionReferenceSql.DeleteOwned,
                    Parameters(("DefinitionVersionId", versionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
                if (await commandExecutor.ExecuteAsync(WorkflowSql.DeleteDefinitionVersion,
                        Parameters(("Id", versionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
                    return Failure<bool>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
            }
            catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.InvalidValue)
            {
                // 另一父版本可能刚提交引用；删除失败必须回滚自身依赖清理，不能留下半删除状态。
                return Failure<bool>(WorkflowErrorCodes.VersionInUse, ErrorType.Conflict);
            }

            if (currentDefinition.LatestPublishedVersionId == versionId)
            {
                await commandExecutor.ExecuteAsync(
                    WorkflowSql.SetLatestDefinitionVersion,
                    Parameters(("Id", definition.Id), ("TenantScopeKey", scope.TenantScopeKey),
                        ("VersionId", nextLatestVersionId), ("UpdatedAtUtc", clock.UtcNow)),
                    token).ConfigureAwait(false);
            }

            return Result<bool>.Success(true);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowDefinitionResponse>> UpdateDraftCoreAsync(
        Guid id, Guid actorUserId, UpdateWorkflowDefinitionDraftRequest request,
        WorkflowManagementScope scope, CancellationToken token)
    {
        var template = WorkflowBusinessTitleRules.NormalizeTemplate(request.BusinessTitleTemplate);
        var definition = await FindDefinitionAsync(id, scope, token).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowDefinitionResponse>();
        }

        if (!WorkflowDefinitionLifecycleRules.AllowsDraftMutation(definition.StatusKey))
        {
            return Failure<WorkflowDefinitionResponse>(WorkflowErrorCodes.DefinitionArchived, ErrorType.Conflict);
        }

        var json = Serialize(request.Draft);
        var affected = await commandExecutor.ExecuteAsync(
            WorkflowSql.UpdateDefinitionDraft,
            Parameters(("DefinitionId", id), ("TenantScopeKey", scope.TenantScopeKey),
                ("DraftJson", json), ("ContentHash", Hash(json)), ("UpdatedById", actorUserId),
                ("UpdatedAtUtc", clock.UtcNow), ("ExpectedRevision", request.ExpectedRevision)), token).ConfigureAwait(false);
        if (affected != 1)
        {
            return await ResolveMutationFailureAsync<WorkflowDefinitionResponse>(id, scope, token).ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
            WorkflowSql.UpdateDefinitionBusinessTitleTemplate,
            Parameters(("Id", id), ("TenantScopeKey", scope.TenantScopeKey),
                ("BusinessTitleTemplate", template), ("UpdatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);

        return await GetAsync(id, token).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowDefinitionVersionResponse>> PublishCoreAsync(
        Guid id, Guid actorUserId, PublishWorkflowDefinitionRequest request, CancellationToken token)
    {
        if (request.ExpectedRevision < 1 || request.FormVersionId == Guid.Empty)
        {
            return Invalid<WorkflowDefinitionVersionResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowDefinitionVersionResponse>(scope, token).ConfigureAwait(false)
            is { } publishDenied)
        {
            return publishDenied;
        }

        var definition = await FindDefinitionAsync(id, scope, token).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowDefinitionVersionResponse>();
        }

        if (!WorkflowDefinitionLifecycleRules.AllowsPublish(definition.StatusKey))
        {
            return definition.StatusKey == WorkflowDefinitionStatusKeys.Archived
                ? Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionArchived, ErrorType.Conflict)
                : Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionDisabled, ErrorType.Conflict);
        }

        var draft = await FindDraftAsync(id, scope, token).ConfigureAwait(false);
        if (draft is null || draft.DraftRevision != request.ExpectedRevision)
        {
            return RevisionConflict<WorkflowDefinitionVersionResponse>();
        }

        var formVersion = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(
            WorkflowSql.FindFormVersionById,
            Parameters(("Id", request.FormVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (formVersion is null)
        {
            return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.VersionNotPublished, ErrorType.Validation);
        }

        var formDefinition = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormDefinitionRecord>(
                WorkflowSql.FindFormDefinitionById,
                Parameters(("Id", formVersion.FormDefinitionId), ("TenantScopeKey", scope.TenantScopeKey)),
                token)
            .ConfigureAwait(false);
        if (formDefinition is null)
        {
            return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.FormNotFound, ErrorType.Validation);
        }

        if (!WorkflowFormLifecycleRules.AllowsPublish(formDefinition.StatusKey))
        {
            return formDefinition.StatusKey == WorkflowDefinitionStatusKeys.Archived
                ? Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.FormArchived, ErrorType.Conflict)
                : Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.FormDisabled, ErrorType.Conflict);
        }

        var model = Deserialize(draft.DraftJson);
        var formSchema = JsonSerializer.Deserialize(
            formVersion.FormSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (model is not null && formSchema is not null)
        {
            if (!connectors.TryBindDraft(scope.TenantScopeKey, model, formSchema, out var bound))
                return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionFieldPolicyInvalid, ErrorType.Validation);
            model = await WorkflowWorkCalendarBindings.BindAsync(bound!,queryExecutor,scope,token).ConfigureAwait(false);
            if(model is null)return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.CalendarInvalid,ErrorType.Validation);
            var businessBinding = WorkflowBusinessCommandBindings.Bind(model, scope, businessActions);
            if (!businessBinding.IsSuccess) return Result<WorkflowDefinitionVersionResponse>.Failure(businessBinding.Error!);
            model = businessBinding.Value;
            var subprocessBinding = await WorkflowSubprocessBindings.BindAsync(model!, formSchema, scope, queryExecutor, token, definition.Id).ConfigureAwait(false);
            if (!subprocessBinding.IsSuccess) return Result<WorkflowDefinitionVersionResponse>.Failure(subprocessBinding.Error!);
            model = subprocessBinding.Value;
        }
        var compilation = model is null || formSchema is null
            ? null
            : WorkflowDefinitionCompiler.Compile(model, formSchema);
        if (compilation is null || !compilation.IsSuccess)
        {
            return Failure<WorkflowDefinitionVersionResponse>(
                compilation?.ErrorCode ?? WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var ccRecipientUserIds = model!.Nodes
            .Where(node => node.NodeTypeKey == "notify.cc")
            .SelectMany(node => WorkflowCcNodeConfiguration.TryReadPolicy(
                node.Config,
                out var recipients) ? recipients.Sources.Where(source => source.ResolverKindKey == WorkflowAssigneePolicy.SpecifiedUsers).SelectMany(source => source.UserIds) : [])
            .Distinct()
            .ToArray();
        var approvalUserIds = model.Nodes
            .Where(node => WorkflowHumanActivityKinds.IsApproval(node.NodeTypeKey))
            .SelectMany(node => WorkflowApprovalPolicy.TryRead(node.Config, out var policy) && policy is not null
                ? policy.GroupConfiguration is { } groups
                    ? groups.Groups.SelectMany(group => group.ApprovalPolicy?.ApproverUserIds ?? []).ToArray()
                    : policy.ApproverUserIds
                : [])
            .Distinct()
            .ToArray();
        var referencedUserIds = ccRecipientUserIds.Concat(approvalUserIds).Distinct().ToArray();
        IReadOnlySet<Guid> validUserIds;
        if (scope.TenantId.HasValue)
        {
            // Tenant 定义中的审批和抄送身份统一批量复核，禁止逐节点回退查询或跨租户引用。
            var users = await tenantUserDirectory.FindActiveTenantMembersAsync(referencedUserIds, token)
                .ConfigureAwait(false);
            validUserIds = users.Keys.ToHashSet();
        }
        else
        {
            // Host 定义继续限定为活动 Host 用户，禁止把 Tenant 用户写入 Host 版本。
            var users = await hostUserDirectory.FindActiveHostUsersAsync(referencedUserIds, token)
                .ConfigureAwait(false);
            validUserIds = users.Keys.ToHashSet();
        }

        if (approvalUserIds.Any(userId => !validUserIds.Contains(userId)))
        {
            return Failure<WorkflowDefinitionVersionResponse>(
                WorkflowErrorCodes.DefinitionApprovalPolicyInvalid,
                ErrorType.Validation);
        }

        if (ccRecipientUserIds.Any(userId => !validUserIds.Contains(userId)))
        {
            return Failure<WorkflowDefinitionVersionResponse>(
                WorkflowErrorCodes.DefinitionCcRecipientsInvalid,
                ErrorType.Validation);
        }

        var start = model.Nodes.SingleOrDefault(item => item.NodeTypeKey == "start");
        if (start is null || !WorkflowFlowAdministrators.TryRead(start.Config, out var flowAdministrators) ||
            flowAdministrators.Count > 0 && !await assigneePublishValidator.ValidateSourceAsync(
                new(WorkflowAssigneePolicy.SpecifiedUsers, flowAdministrators, [], null, null), scope, token).ConfigureAwait(false))
            return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, ErrorType.Validation);
        foreach (var cc in model.Nodes.Where(node => node.NodeTypeKey == "notify.cc"))
        {
            if (!WorkflowCcNodeConfiguration.TryReadPolicy(cc.Config, out var recipients))
                return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionCcRecipientsInvalid, ErrorType.Validation);
            foreach (var source in recipients.Sources)
                if (!WorkflowAssigneePublishValidator.IsScopeCompatible(source.ResolverKindKey, scope) ||
                    source.ResolverKindKey is not (WorkflowAssigneePolicy.SpecifiedUsers or WorkflowAssigneePolicy.FlowAdministrators) &&
                    !await assigneePublishValidator.ValidateSourceAsync(source, scope, token, allowEmpty: true, flowAdministratorUserIds: flowAdministrators).ConfigureAwait(false))
                    return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionCcRecipientsInvalid, ErrorType.Validation);
        }
        foreach (var node in model.Nodes.Where(item => WorkflowHumanActivityKinds.IsFormActivity(item.NodeTypeKey)))
        {
            IReadOnlyList<WorkflowAssigneePolicy> policies;
            if (node.NodeTypeKey == WorkflowHumanActivityKinds.GroupApproval)
            {
                if (!WorkflowGroupApprovalConfiguration.TryRead(node.Config, out var groups))
                    return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, ErrorType.Validation);
                // 已知显式身份不可跨组重复；动态来源仍在激活时按实际成员集合再次验证。
                var explicitUsers = groups!.Groups.SelectMany(group => group.ApprovalPolicy?.ApproverUserIds ?? []).ToArray();
                if (explicitUsers.Distinct().Count() != explicitUsers.Length)
                    return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionApprovalPolicyInvalid, ErrorType.Validation);
                policies = groups.Groups.Select(group => group.AssigneePolicy).ToArray();
            }
            else
            {
                if (!WorkflowAssigneePolicy.TryRead(node.Config, out var policy))
                    return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, ErrorType.Validation);
                policies = [policy!];
            }
            foreach (var policy in policies)
            {
                if (!await assigneePublishValidator.ValidatePolicyAsync(policy, scope, token, flowAdministrators).ConfigureAwait(false))
                    return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, ErrorType.Validation);
            }
        }

        foreach (var node in model.Nodes.Where(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType))
        {
            if (!WorkflowSubprocessPolicy.TryRead(node.Config, null, out var subprocess))
                return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionSubprocessInvalid, ErrorType.Validation);
            if (subprocess.Multi?.RoleId is { } roleId && !await assigneePublishValidator.ValidateSourceAsync(
                new(WorkflowAssigneePolicy.RoleMembers, [], [roleId], null, null), scope, token, allowEmpty: true).ConfigureAwait(false))
                return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionSubprocessInvalid, ErrorType.Validation);
        }
        var voteForms = await PrepareVoteFormsAsync(model, scope, token).ConfigureAwait(false);
        if (!voteForms.IsSuccess) return Result<WorkflowDefinitionVersionResponse>.Failure(voteForms.Error!);
        return await transaction.ExecuteResultAsync(
            transactionToken => PersistPublishedVersionAsync(
                id,
                actorUserId,
                request,
                scope,
                definition,
                model,
                compilation.Value!,
                voteForms.Value!,
                transactionToken),
            token).ConfigureAwait(false);
    }

    /// <summary>在 Workflow 本地事务内认领草稿并写入不可变版本、最新指针和领域审计。</summary>
    /// <param name="id">工作流定义标识。</param>
    /// <param name="actorUserId">可信当前操作人标识。</param>
    /// <param name="request">发布请求。</param>
    /// <param name="scope">发布前解析的可信管理作用域。</param>
    /// <param name="model">已编译的定义模型。</param>
    /// <param name="artifact">已生成的规范化定义产物。</param>
    /// <param name="token">本地事务取消令牌。</param>
    /// <returns>发布成功后的不可变版本，或并发/资源错误。</returns>
    private async Task<Result<WorkflowDefinitionVersionResponse>> PersistPublishedVersionAsync(
        Guid id,
        Guid actorUserId,
        PublishWorkflowDefinitionRequest request,
        WorkflowManagementScope scope,
        WorkflowDefinitionRecord definition,
        WorkflowDefinitionDraft model,
        WorkflowCompiledArtifact artifact,
        IReadOnlyList<WorkflowPreparedVoteFormBinding> voteForms,
        CancellationToken token)
    {
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(WorkflowVersionReferenceSql.LockDefinition,
            Parameters(("DefinitionId", id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var currentDefinition = await FindDefinitionAsync(id, scope, token).ConfigureAwait(false);
        if (currentDefinition is null) return NotFound<WorkflowDefinitionVersionResponse>();
        if (!WorkflowDefinitionLifecycleRules.AllowsPublish(currentDefinition.StatusKey))
            return Failure<WorkflowDefinitionVersionResponse>(currentDefinition.StatusKey == WorkflowDefinitionStatusKeys.Archived
                ? WorkflowErrorCodes.DefinitionArchived : WorkflowErrorCodes.DefinitionDisabled, ErrorType.Conflict);
        var claimed = await commandExecutor.ExecuteAsync(
            WorkflowSql.ClaimDefinitionDraftForPublish,
            Parameters(("DefinitionId", id), ("TenantScopeKey", scope.TenantScopeKey),
                ("ExpectedRevision", request.ExpectedRevision), ("UpdatedById", actorUserId),
                ("UpdatedAtUtc", now)), token).ConfigureAwait(false);
        if (claimed != 1)
        {
            return RevisionConflict<WorkflowDefinitionVersionResponse>();
        }

        var number = await queryExecutor.QuerySingleOrDefaultAsync<int>(
            WorkflowSql.FindNextDefinitionVersionNumber,
            Parameters(("DefinitionId", id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var versionId = idGenerator.NewId();
        try
        {
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertDefinitionVersion,
                Parameters(("Id", versionId), ("DefinitionId", id), ("FormVersionId", request.FormVersionId),
                    ("VersionNumber", number), ("SchemaVersion", model!.SchemaVersion),
                    ("CanonicalJson", artifact.CanonicalJson), ("ContentHash", artifact.ContentHash),
                    ("BusinessTitleTemplate", definition.BusinessTitleTemplate),
                    ("PublishedById", actorUserId), ("PublishedAtUtc", now)), token).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return RevisionConflict<WorkflowDefinitionVersionResponse>();
        }

        var childVersions = model.Nodes.Where(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType)
            .Select(node => node.Config.GetProperty("subprocessPolicy").GetProperty("definitionVersionId").GetGuid()).Distinct().Order().ToArray();
        try
        {
            foreach (var childVersionId in childVersions)
                if (await commandExecutor.ExecuteAsync(WorkflowVersionReferenceSql.Insert,
                        Parameters(("DefinitionId", id), ("ParentVersionId", versionId), ("ChildVersionId", childVersionId),
                            ("TenantScopeKey", scope.TenantScopeKey), ("CreatedAtUtc", now)), token).ConfigureAwait(false) != 1)
                    return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionSubprocessInvalid, ErrorType.Conflict);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.InvalidValue)
        {
            return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionSubprocessInvalid, ErrorType.Conflict);
        }

        foreach (var binding in voteForms)
        {
            if (await commandExecutor.ExecuteAsync(WorkflowVoteSql.InsertFormBinding,
                Parameters(("Id", idGenerator.NewId()), ("DefinitionVersionId", versionId), ("NodeKey", binding.NodeKey),
                    ("BallotFormVersionId", binding.FormVersionId), ("FormSchemaHash", binding.FormSchemaHash),
                    ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
                return Failure<WorkflowDefinitionVersionResponse>(WorkflowErrorCodes.DefinitionVotePolicyInvalid, ErrorType.Conflict);
        }

        var updated = await commandExecutor.ExecuteAsync(
            WorkflowSql.SetLatestDefinitionVersion,
            Parameters(("Id", id), ("TenantScopeKey", scope.TenantScopeKey),
                ("VersionId", versionId), ("UpdatedAtUtc", now)), token).ConfigureAwait(false);
        if (updated != 1)
        {
            return NotFound<WorkflowDefinitionVersionResponse>();
        }

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertDomainAudit,
            Parameters(("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", null),
                ("OperationKey", "definition.publish"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "definition"), ("ResourceId", id),
                ("OutcomeKey", "succeeded"),
                ("DetailJson", $"{{\"versionId\":\"{versionId:D}\",\"formVersionId\":\"{request.FormVersionId:D}\"}}"),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        return Result<WorkflowDefinitionVersionResponse>.Success(new(
            versionId, id, request.FormVersionId, number, model.SchemaVersion,
            artifact.CanonicalJson, artifact.ContentHash, definition.BusinessTitleTemplate,
            actorUserId, now));
    }

    private async Task<Result<T>?> TryDenyTenantMutationAsync<T>(
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        if (await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope, featureEntitlements, cancellationToken)
                .ConfigureAwait(false) is { } denial)
        {
            return WorkflowTenantFeatureEntitlementGate.Deny<T>(denial);
        }

        return null;
    }

    private Task<WorkflowDefinitionRecord?> FindDefinitionAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        queryExecutor.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(WorkflowSql.FindDefinitionById,
            Parameters(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);

    private Task<WorkflowDefinitionDraftRecord?> FindDraftAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        queryExecutor.QuerySingleOrDefaultAsync<WorkflowDefinitionDraftRecord>(WorkflowSql.FindDefinitionDraftByDefinition,
            Parameters(("DefinitionId", id), ("TenantScopeKey", scope.TenantScopeKey)), token);

    private static WorkflowDefinitionResponse? Map(
        WorkflowDefinitionRecord definition, WorkflowDefinitionDraftRecord draft)
    {
        var model = Deserialize(draft.DraftJson);
        return model is null ? null : new(
            definition.Id, definition.DefinitionKey, model, draft.DraftRevision,
            definition.LatestPublishedVersionId, definition.BusinessTitleTemplate, definition.StatusKey,
            definition.Version, definition.CreatedAtUtc, definition.UpdatedAtUtc);
    }

    private async Task<Result<T>> ResolveMutationFailureAsync<T>(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        await FindDefinitionAsync(id, scope, token).ConfigureAwait(false) is null ? NotFound<T>() : RevisionConflict<T>();

    private static WorkflowDefinitionVersionResponse Map(WorkflowDefinitionVersionRecord row) =>
        new(row.Id, row.DefinitionId, row.FormVersionId, row.VersionNumber, row.SchemaVersion,
            row.CanonicalJson, row.ContentHash, row.BusinessTitleTemplate, row.PublishedById, row.PublishedAtUtc);

    /// <summary>独立表单在发布事务外按实际不同版本一次读取；不以客户端设计器内容替代正式版本。</summary>
    private async Task<Result<IReadOnlyList<WorkflowPreparedVoteFormBinding>>> PrepareVoteFormsAsync(
        WorkflowDefinitionDraft model, WorkflowManagementScope scope, CancellationToken token)
    {
        var nodes = model.Nodes.Where(node => node.NodeTypeKey == WorkflowHumanActivityKinds.Vote).ToArray();
        if (nodes.Length == 0) return Result<IReadOnlyList<WorkflowPreparedVoteFormBinding>>.Success([]);
        var policies = new List<(string NodeKey, WorkflowVotePolicy Policy)>();
        foreach (var node in nodes)
        {
            if (!WorkflowVotePolicy.TryRead(node.Config, out var policy))
                return Failure<IReadOnlyList<WorkflowPreparedVoteFormBinding>>(WorkflowErrorCodes.DefinitionVotePolicyInvalid, ErrorType.Validation);
            policies.Add((node.NodeKey, policy!));
        }
        var candidates = await queryExecutor.QueryAsync<WorkflowVoteFormCandidateRecord>(WorkflowVoteSql.FindFormCandidates,
            Parameters(("FormVersionIds", policies.Select(item => item.Policy.BallotFormVersionId).Distinct().ToArray()),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var byVersion = candidates.ToDictionary(form => form.Id);
        foreach (var form in candidates)
        {
            if (form.FormStatusKey != "active" || JsonSerializer.Deserialize(form.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema) is null ||
                !WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(queryExecutor, form.Id, scope, token).ConfigureAwait(false)))
                return Failure<IReadOnlyList<WorkflowPreparedVoteFormBinding>>(WorkflowErrorCodes.DefinitionVotePolicyInvalid, ErrorType.Validation);
        }
        var prepared = new List<WorkflowPreparedVoteFormBinding>();
        foreach (var item in policies)
        {
            if (!byVersion.TryGetValue(item.Policy.BallotFormVersionId, out var form))
                return Failure<IReadOnlyList<WorkflowPreparedVoteFormBinding>>(WorkflowErrorCodes.DefinitionVotePolicyInvalid, ErrorType.Validation);
            prepared.Add(new(item.NodeKey, form.Id, Hash(form.FormSchemaJson)));
        }
        return Result<IReadOnlyList<WorkflowPreparedVoteFormBinding>>.Success(prepared);
    }

    /// <summary>传入本地发布事务的表单绑定只有固定版本和摘要，不携带不可信租户或临时设计内容。</summary>
    private sealed record WorkflowPreparedVoteFormBinding(string NodeKey, Guid FormVersionId, string FormSchemaHash);

    private static string Serialize(WorkflowDefinitionDraft draft) =>
        JsonSerializer.Serialize(draft, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
    private static WorkflowDefinitionDraft? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string? NormalizeKey(string? value)
    {
        var key = value?.Trim().ToLowerInvariant();
        return key is { Length: >= 3 and <= 128 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? key : null;
    }

    private static Dictionary<string, object?> Parameters(params (string Name, object? Value)[] pairs) => WorkflowSqlParameters.Create(pairs);
    private static Result<T> NotFound<T>() => Failure<T>(WorkflowErrorCodes.DefinitionNotFound, ErrorType.NotFound);
    private static Result<T> Invalid<T>() => Failure<T>(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
    private static Result<T> RevisionConflict<T>() => Failure<T>(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static Result<T> Conflict<T>(string code) => Failure<T>(code, ErrorType.Conflict);
    private static Result<T> Failure<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The workflow definition operation failed.", type));
}
