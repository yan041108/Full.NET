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
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Features.ManageForms;

/// <summary>维护当前可信作用域的表单草稿，并以追加方式发布不可变版本。</summary>
internal sealed class WorkflowFormManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    ITenantFeatureEntitlementPort featureEntitlements)
{
    private const int ComponentCatalogVersion = 1;

    public async Task<Result<IReadOnlyList<WorkflowFormResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<WorkflowFormDefinitionRecord>(
                WorkflowSql.ListFormDefinitions,
                Parameters(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<WorkflowFormResponse>>.Success(rows.Select(Map).ToArray());
    }

    public async Task<Result<WorkflowFormResponse>> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var row = await FindAsync(id, scope, cancellationToken).ConfigureAwait(false);
        return row is null ? NotFound<WorkflowFormResponse>() : Result<WorkflowFormResponse>.Success(Map(row));
    }

    public async Task<Result<WorkflowFormVersionResponse>> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(
                WorkflowSql.FindFormVersionById,
                Parameters(("Id", versionId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFound<WorkflowFormVersionResponse>()
            : Result<WorkflowFormVersionResponse>.Success(Map(row));
    }

    public async Task<Result<WorkflowFormResponse>> CreateAsync(
        Guid actorUserId,
        CreateWorkflowFormRequest request,
        CancellationToken cancellationToken = default)
    {
        var formKey = NormalizeKey(request.FormKey);
        if (formKey is null)
        {
            return Invalid<WorkflowFormResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowFormResponse>(scope, cancellationToken).ConfigureAwait(false)
            is { } createDenied)
        {
            return createDenied;
        }

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        var draftJson = Serialize(request.Draft);
        try
        {
            return await transaction.ExecuteResultAsync(
                    async token =>
                    {
                        await commandExecutor.ExecuteAsync(
                                WorkflowSql.InsertFormDefinition,
                                Parameters(
                                    ("Id", id), ("TenantId", scope.TenantId),
                                    ("ScopeKey", scope.ScopeKey), ("TenantScopeKey", scope.TenantScopeKey),
                                    ("FormKey", formKey), ("DraftSchemaJson", draftJson),
                                    ("CreatedById", actorUserId), ("CreatedAtUtc", now)),
                                token)
                            .ConfigureAwait(false);
                        return Result<WorkflowFormResponse>.Success(new(
                            id, formKey, request.Draft, 1, null,
                            WorkflowDefinitionStatusKeys.Active, 1, now, null));
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Conflict<WorkflowFormResponse>(WorkflowErrorCodes.FormKeyExists);
        }
    }

    public Task<Result<WorkflowFormResponse>> UpdateDraftAsync(
        Guid id,
        UpdateWorkflowFormDraftRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateDraftCoreAsync(id, request, token),
            cancellationToken);

    public Task<Result<WorkflowFormVersionResponse>> PublishAsync(
        Guid id,
        Guid actorUserId,
        PublishWorkflowFormRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => PublishCoreAsync(id, actorUserId, request, token),
            cancellationToken);

    /// <summary>在乐观并发保护下变更表单启停或归档状态。</summary>
    /// <param name="id">表单定义标识。</param>
    /// <param name="request">目标状态与期望版本。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>更新后的表单投影，或稳定业务错误。</returns>
    public async Task<Result<WorkflowFormResponse>> SetStatusAsync(
        Guid id,
        SetWorkflowFormStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!WorkflowFormLifecycleRules.IsKnownStatusKey(request.StatusKey) || request.ExpectedVersion < 1)
        {
            return Invalid<WorkflowFormResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowFormResponse>(scope, cancellationToken).ConfigureAwait(false)
            is { } statusDenied)
        {
            return statusDenied;
        }

        var definition = await FindAsync(id, scope, cancellationToken).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowFormResponse>();
        }

        if (definition.StatusKey == WorkflowDefinitionStatusKeys.Archived)
        {
            return Failure<WorkflowFormResponse>(WorkflowErrorCodes.FormArchived, ErrorType.Conflict);
        }

        if (!WorkflowFormLifecycleRules.CanTransition(definition.StatusKey, request.StatusKey))
        {
            return Failure<WorkflowFormResponse>(WorkflowErrorCodes.FormStatusInvalid, ErrorType.Validation);
        }

        if (definition.Version != request.ExpectedVersion)
        {
            return RevisionConflict<WorkflowFormResponse>();
        }

        var affected = await commandExecutor.ExecuteAsync(
                WorkflowSql.UpdateFormStatus,
                Parameters(("Id", id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("StatusKey", request.StatusKey), ("UpdatedAtUtc", clock.UtcNow),
                    ("ExpectedVersion", request.ExpectedVersion)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return RevisionConflict<WorkflowFormResponse>();
        }

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>列出表单下全部不可变版本。</summary>
    /// <param name="formId">表单定义标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>版本列表或稳定业务错误。</returns>
    public async Task<Result<IReadOnlyList<WorkflowFormVersionResponse>>> ListVersionsAsync(
        Guid formId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await FindAsync(formId, scope, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound<IReadOnlyList<WorkflowFormVersionResponse>>();
        }

        var rows = await queryExecutor.QueryAsync<WorkflowFormVersionRecord>(
                WorkflowSql.ListFormVersions,
                Parameters(("FormDefinitionId", formId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<WorkflowFormVersionResponse>>.Success(rows.Select(Map).ToArray());
    }

    /// <summary>删除未被运行实例引用的不可变表单版本。</summary>
    /// <param name="versionId">待删除表单版本标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public async Task<Result<bool>> DeleteVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
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

        var version = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(
                WorkflowSql.FindFormVersionById,
                Parameters(("Id", versionId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return Failure<bool>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var definition = await FindAsync(version.FormDefinitionId, scope, cancellationToken).ConfigureAwait(false);
        if (definition is null)
        {
            return Failure<bool>(WorkflowErrorCodes.FormNotFound, ErrorType.NotFound);
        }

        var runningCount = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                WorkflowSql.CountRunningInstancesByFormVersion,
                Parameters(("FormVersionId", versionId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (runningCount > 0)
        {
            return Failure<bool>(WorkflowErrorCodes.FormVersionInUse, ErrorType.Conflict);
        }

        var remainingVersions = (await queryExecutor.QueryAsync<WorkflowFormVersionRecord>(
                WorkflowSql.ListFormVersions,
                Parameters(("FormDefinitionId", version.FormDefinitionId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false))
            .Where(item => item.Id != versionId)
            .ToArray();
        var nextLatestVersionId = remainingVersions.MaxBy(item => item.VersionNumber)?.Id;

        return await transaction.ExecuteResultAsync(async token =>
        {
            var affected = await commandExecutor.ExecuteAsync(
                    WorkflowSql.DeleteFormVersion,
                    Parameters(("Id", versionId), ("TenantScopeKey", scope.TenantScopeKey)),
                    token)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return Failure<bool>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
            }

            if (definition.LatestPublishedVersionId == versionId)
            {
                await commandExecutor.ExecuteAsync(
                        WorkflowSql.SetLatestFormVersion,
                        Parameters(("Id", definition.Id), ("TenantScopeKey", scope.TenantScopeKey),
                            ("VersionId", nextLatestVersionId), ("UpdatedAtUtc", clock.UtcNow)),
                        token)
                    .ConfigureAwait(false);
            }

            return Result<bool>.Success(true);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowFormResponse>> UpdateDraftCoreAsync(
        Guid id,
        UpdateWorkflowFormDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision < 1)
        {
            return Invalid<WorkflowFormResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowFormResponse>(scope, cancellationToken).ConfigureAwait(false)
            is { } draftDenied)
        {
            return draftDenied;
        }

        var definition = await FindAsync(id, scope, cancellationToken).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowFormResponse>();
        }

        if (!WorkflowFormLifecycleRules.AllowsDraftMutation(definition.StatusKey))
        {
            return Failure<WorkflowFormResponse>(WorkflowErrorCodes.FormArchived, ErrorType.Conflict);
        }

        var affected = await commandExecutor.ExecuteAsync(
                WorkflowSql.UpdateFormDraft,
                Parameters(
                    ("Id", id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("DraftSchemaJson", Serialize(request.Draft)),
                    ("UpdatedAtUtc", clock.UtcNow), ("ExpectedRevision", request.ExpectedRevision)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return await ResolveMutationFailureAsync<WorkflowFormResponse>(id, scope, cancellationToken)
                .ConfigureAwait(false);
        }

        var row = await FindAsync(id, scope, cancellationToken).ConfigureAwait(false);
        return Result<WorkflowFormResponse>.Success(Map(row!));
    }

    private async Task<Result<WorkflowFormVersionResponse>> PublishCoreAsync(
        Guid id,
        Guid actorUserId,
        PublishWorkflowFormRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision < 1)
        {
            return Invalid<WorkflowFormVersionResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowFormVersionResponse>(scope, cancellationToken)
                .ConfigureAwait(false) is { } publishDenied)
        {
            return publishDenied;
        }

        var definition = await FindAsync(id, scope, cancellationToken).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowFormVersionResponse>();
        }

        if (!WorkflowFormLifecycleRules.AllowsPublish(definition.StatusKey))
        {
            return definition.StatusKey == WorkflowDefinitionStatusKeys.Archived
                ? Failure<WorkflowFormVersionResponse>(WorkflowErrorCodes.FormArchived, ErrorType.Conflict)
                : Failure<WorkflowFormVersionResponse>(WorkflowErrorCodes.FormDisabled, ErrorType.Conflict);
        }

        if (definition.DraftRevision != request.ExpectedRevision)
        {
            return RevisionConflict<WorkflowFormVersionResponse>();
        }

        var schema = Deserialize(definition.DraftSchemaJson);
        if (schema is null)
        {
            return Invalid<WorkflowFormVersionResponse>();
        }

        var compilation = WorkflowFormCompiler.Compile(schema);
        if (!compilation.IsSuccess)
        {
            return Failure<WorkflowFormVersionResponse>(compilation.ErrorCode!, ErrorType.Validation);
        }

        var versionNumber = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                WorkflowSql.FindNextFormVersionNumber,
                Parameters(("FormDefinitionId", id), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var versionId = idGenerator.NewId();
        var now = clock.UtcNow;
        var artifact = compilation.Value!;
        try
        {
            await commandExecutor.ExecuteAsync(
                    WorkflowSql.InsertFormVersion,
                    Parameters(
                        ("Id", versionId), ("FormDefinitionId", id), ("VersionNumber", versionNumber),
                        ("SchemaVersion", schema.SchemaVersion), ("AdapterVersion", schema.AdapterVersion),
                        ("ComponentCatalogVersion", ComponentCatalogVersion),
                        ("FormSchemaJson", artifact.CanonicalJson),
                        ("WebRenderSchemaJson", artifact.CanonicalJson),
                        ("ContentHash", artifact.ContentHash),
                        ("PublishedById", actorUserId), ("PublishedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return RevisionConflict<WorkflowFormVersionResponse>();
        }

        var affected = await commandExecutor.ExecuteAsync(
                WorkflowSql.PublishFormVersion,
                Parameters(
                    ("Id", id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("VersionId", versionId), ("UpdatedAtUtc", now),
                    ("ExpectedRevision", request.ExpectedRevision)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return RevisionConflict<WorkflowFormVersionResponse>();
        }

        await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertDomainAudit,
                Parameters(
                    ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                    ("ScopeKey", scope.ScopeKey), ("InstanceId", null),
                    ("OperationKey", "form.publish"), ("ActorUserId", actorUserId),
                    ("ResourceTypeKey", "form-definition"), ("ResourceId", id),
                    ("OutcomeKey", "succeeded"),
                    ("DetailJson", $"{{\"versionId\":\"{versionId:D}\"}}"),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<WorkflowFormVersionResponse>.Success(new(
            versionId, id, versionNumber, schema.SchemaVersion, schema.AdapterVersion,
            ComponentCatalogVersion, artifact.CanonicalJson, artifact.CanonicalJson,
            artifact.ContentHash, actorUserId, now));
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

    private Task<WorkflowFormDefinitionRecord?> FindAsync(
        Guid id,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormDefinitionRecord>(
            WorkflowSql.FindFormDefinitionById,
            Parameters(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);

    private async Task<Result<T>> ResolveMutationFailureAsync<T>(
        Guid id,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken) =>
        await FindAsync(id, scope, cancellationToken).ConfigureAwait(false) is null
            ? NotFound<T>()
            : RevisionConflict<T>();

    private static WorkflowFormResponse Map(WorkflowFormDefinitionRecord row) =>
        new(row.Id, row.FormKey, Deserialize(row.DraftSchemaJson)!, row.DraftRevision,
            row.LatestPublishedVersionId, row.StatusKey, row.Version, row.CreatedAtUtc, row.UpdatedAtUtc);

    private static WorkflowFormVersionResponse Map(WorkflowFormVersionRecord row) =>
        new(row.Id, row.FormDefinitionId, row.VersionNumber, row.SchemaVersion,
            row.AdapterVersion, row.ComponentCatalogVersion, row.FormSchemaJson,
            row.WebRenderSchemaJson, row.ContentHash, row.PublishedById, row.PublishedAtUtc);

    private static string Serialize(WorkflowFormSchema schema) =>
        JsonSerializer.Serialize(schema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);

    private static WorkflowFormSchema? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);

    private static string? NormalizeKey(string? value)
    {
        var key = value?.Trim().ToLowerInvariant();
        return key is { Length: >= 3 and <= 128 } &&
               key.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
            ? key
            : null;
    }

    private static Dictionary<string, object?> Parameters(params (string Name, object? Value)[] pairs) =>
        WorkflowSqlParameters.Create(pairs);

    private static Result<T> NotFound<T>() => Failure<T>(WorkflowErrorCodes.FormNotFound, ErrorType.NotFound);
    private static Result<T> Invalid<T>() => Failure<T>(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
    private static Result<T> RevisionConflict<T>() => Failure<T>(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static Result<T> Conflict<T>(string code) => Failure<T>(code, ErrorType.Conflict);
    private static Result<T> Failure<T>(string code, ErrorType type) =>
        Result<T>.Failure(new Error(code, "The workflow form operation failed.", type));
}
