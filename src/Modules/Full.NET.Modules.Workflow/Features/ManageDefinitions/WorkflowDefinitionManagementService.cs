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

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>维护流程定义草稿，并在可信作用域内绑定不可变表单版本后发布。</summary>
internal sealed class WorkflowDefinitionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
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
        if (definitionKey is null)
        {
            return Invalid<WorkflowDefinitionResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
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
                        ("CreatedById", actorUserId), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(
                    WorkflowSql.InsertDefinitionDraft,
                    Parameters(("Id", draftId), ("DefinitionId", definitionId),
                        ("DraftJson", draftJson), ("ContentHash", Hash(draftJson)),
                        ("UpdatedById", actorUserId), ("UpdatedAtUtc", now)), token).ConfigureAwait(false);
                return Result<WorkflowDefinitionResponse>.Success(new(
                    definitionId, definitionKey, request.Draft, 1, null, 1, now, null));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Conflict<WorkflowDefinitionResponse>(WorkflowErrorCodes.DefinitionKeyExists);
        }
    }

    public Task<Result<WorkflowDefinitionResponse>> UpdateDraftAsync(
        Guid id, Guid actorUserId, UpdateWorkflowDefinitionDraftRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(token => UpdateDraftCoreAsync(id, actorUserId, request, token), cancellationToken);

    public Task<Result<WorkflowDefinitionVersionResponse>> PublishAsync(
        Guid id, Guid actorUserId, PublishWorkflowDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(token => PublishCoreAsync(id, actorUserId, request, token), cancellationToken);

    private async Task<Result<WorkflowDefinitionResponse>> UpdateDraftCoreAsync(
        Guid id, Guid actorUserId, UpdateWorkflowDefinitionDraftRequest request, CancellationToken token)
    {
        if (request.ExpectedRevision < 1)
        {
            return Invalid<WorkflowDefinitionResponse>();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
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
        var definition = await FindDefinitionAsync(id, scope, token).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowDefinitionVersionResponse>();
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

        var model = Deserialize(draft.DraftJson);
        var formSchema = JsonSerializer.Deserialize(
            formVersion.FormSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var compilation = model is null || formSchema is null
            ? null
            : WorkflowDefinitionCompiler.Compile(model, formSchema);
        if (compilation is null || !compilation.IsSuccess)
        {
            return Failure<WorkflowDefinitionVersionResponse>(
                compilation?.ErrorCode ?? WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var now = clock.UtcNow;
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
        var artifact = compilation.Value!;
        try
        {
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertDefinitionVersion,
                Parameters(("Id", versionId), ("DefinitionId", id), ("FormVersionId", request.FormVersionId),
                    ("VersionNumber", number), ("SchemaVersion", model!.SchemaVersion),
                    ("CanonicalJson", artifact.CanonicalJson), ("ContentHash", artifact.ContentHash),
                    ("PublishedById", actorUserId), ("PublishedAtUtc", now)), token).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return RevisionConflict<WorkflowDefinitionVersionResponse>();
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
            Parameters(("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", null),
                ("OperationKey", "definition.publish"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "definition"), ("ResourceId", id),
                ("OutcomeKey", "succeeded"),
                ("DetailJson", $"{{\"versionId\":\"{versionId:D}\",\"formVersionId\":\"{request.FormVersionId:D}\"}}"),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        return Result<WorkflowDefinitionVersionResponse>.Success(new(
            versionId, id, request.FormVersionId, number, model.SchemaVersion,
            artifact.CanonicalJson, artifact.ContentHash, actorUserId, now));
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
            definition.LatestPublishedVersionId, definition.Version,
            definition.CreatedAtUtc, definition.UpdatedAtUtc);
    }

    private async Task<Result<T>> ResolveMutationFailureAsync<T>(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        await FindDefinitionAsync(id, scope, token).ConfigureAwait(false) is null ? NotFound<T>() : RevisionConflict<T>();

    private static WorkflowDefinitionVersionResponse Map(WorkflowDefinitionVersionRecord row) =>
        new(row.Id, row.DefinitionId, row.FormVersionId, row.VersionNumber, row.SchemaVersion,
            row.CanonicalJson, row.ContentHash, row.PublishedById, row.PublishedAtUtc);

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
