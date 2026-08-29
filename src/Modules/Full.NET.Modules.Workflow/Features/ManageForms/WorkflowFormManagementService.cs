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

namespace Full.NET.Modules.Workflow.Features.ManageForms;

/// <summary>维护当前可信作用域的表单草稿，并以追加方式发布不可变版本。</summary>
internal sealed class WorkflowFormManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
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
                            id, formKey, request.Draft, 1, null, now, null));
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
        var definition = await FindAsync(id, scope, cancellationToken).ConfigureAwait(false);
        if (definition is null)
        {
            return NotFound<WorkflowFormVersionResponse>();
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

        return Result<WorkflowFormVersionResponse>.Success(new(
            versionId, id, versionNumber, schema.SchemaVersion, schema.AdapterVersion,
            ComponentCatalogVersion, artifact.CanonicalJson, artifact.CanonicalJson,
            artifact.ContentHash, actorUserId, now));
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
            row.LatestPublishedVersionId, row.CreatedAtUtc, row.UpdatedAtUtc);

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
