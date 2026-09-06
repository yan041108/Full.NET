using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ManageDataSources;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageDefinitions;

/// <summary>报表定义草稿维护与发布版本。</summary>
internal sealed class ReportingDefinitionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ReportingDefinitionQueryService queries,
    ReportingDataSourceQueryService dataSourceQueries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<ReportingDefinitionResponse>> CreateAsync(
        CreateReportingDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => CreateCoreAsync(request, token), cancellationToken);

    public Task<Result<ReportingDefinitionResponse>> UpdateAsync(
        Guid definitionId,
        UpdateReportingDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => UpdateCoreAsync(definitionId, request, token), cancellationToken);

    public Task<Result<ReportingDefinitionVersionResponse>> PublishAsync(
        Guid definitionId,
        Guid publishedByUserId,
        PublishReportingDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => PublishCoreAsync(definitionId, publishedByUserId, request, token),
            cancellationToken);

    public Task<Result<bool>> DeleteAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => DeleteCoreAsync(definitionId, token), cancellationToken);

    private async Task<Result<ReportingDefinitionResponse>> CreateCoreAsync(
        CreateReportingDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateDraftAsync(
                request.GroupId,
                request.DataSourceId,
                request.DefinitionKey,
                request.Name,
                request.QueryPortKey,
                request.ParameterSchema,
                request.LayoutConfigJson,
                definitionId: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<ReportingDefinitionResponse>.Failure(validation.Error!);
        }

        var definitionId = idGenerator.NewId();
        var now = clock.UtcNow;
        var parameterSchemaJson = ReportingDefinitionJson.SerializeParameterSchema(request.ParameterSchema);
        var layoutConfigJson = validation.Value!.LayoutConfigJson;
        await commandExecutor.ExecuteAsync(
                ReportingDefinitionSql.InsertDefinition,
                ReportingSqlParameters.Create(
                    ("Id", definitionId),
                    ("GroupId", request.GroupId),
                    ("DataSourceId", request.DataSourceId),
                    ("DefinitionKey", request.DefinitionKey.Trim()),
                    ("Name", request.Name.Trim()),
                    ("Description", NormalizeOptional(request.Description)),
                    ("QueryPortKey", request.QueryPortKey.Trim()),
                    ("ParameterSchemaJson", parameterSchemaJson),
                    ("LayoutConfigJson", layoutConfigJson),
                    ("LatestPublishedVersionNumber", 0),
                    ("IsEnabled", request.IsEnabled),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        return await queries.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ReportingDefinitionResponse>> UpdateCoreAsync(
        Guid definitionId,
        UpdateReportingDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
                ReportingDefinitionSql.FindDefinitionById,
                ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDefinition();
        }

        var validation = await ValidateDraftAsync(
                request.GroupId,
                request.DataSourceId,
                current.DefinitionKey,
                request.Name,
                request.QueryPortKey,
                request.ParameterSchema,
                request.LayoutConfigJson,
                definitionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<ReportingDefinitionResponse>.Failure(validation.Error!);
        }

        var affected = await commandExecutor.ExecuteAsync(
                ReportingDefinitionSql.UpdateDefinition,
                ReportingSqlParameters.Create(
                    ("DefinitionId", definitionId),
                    ("GroupId", request.GroupId),
                    ("DataSourceId", request.DataSourceId),
                    ("Name", request.Name.Trim()),
                    ("Description", NormalizeOptional(request.Description)),
                    ("QueryPortKey", request.QueryPortKey.Trim()),
                    ("ParameterSchemaJson", ReportingDefinitionJson.SerializeParameterSchema(request.ParameterSchema)),
                    ("LayoutConfigJson", validation.Value!.LayoutConfigJson),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConflictDefinition();
        }

        return await queries.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ReportingDefinitionVersionResponse>> PublishCoreAsync(
        Guid definitionId,
        Guid publishedByUserId,
        PublishReportingDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
                ReportingDefinitionSql.FindDefinitionById,
                ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundVersion();
        }

        if (current.Version != request.Version)
        {
            return ConflictVersion();
        }

        var dataSource = await dataSourceQueries.GetByIdAsync(current.DataSourceId, cancellationToken)
            .ConfigureAwait(false);
        if (!dataSource.IsSuccess || dataSource.Value is null || !dataSource.Value.IsEnabled)
        {
            return InvalidVersion("The reporting data source is missing or disabled.");
        }

        var queryPort = ReportingQueryPortCatalog.TryGet(current.QueryPortKey);
        if (queryPort is null
            || !queryPort.SupportedProviderKeys.Contains(
                dataSource.Value.ProviderKey,
                StringComparer.Ordinal))
        {
            return InvalidVersion("The selected query port is not supported by the data source provider.");
        }

        var versionNumber = current.LatestPublishedVersionNumber + 1;
        var versionId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                ReportingDefinitionSql.InsertVersion,
                ReportingSqlParameters.Create(
                    ("Id", versionId),
                    ("DefinitionId", definitionId),
                    ("VersionNumber", versionNumber),
                    ("DataSourceId", current.DataSourceId),
                    ("QueryPortKey", current.QueryPortKey),
                    ("ParameterSchemaJson", current.ParameterSchemaJson),
                    ("LayoutConfigJson", current.LayoutConfigJson),
                    ("ChangeNote", NormalizeOptional(request.ChangeNote)),
                    ("PublishedByUserId", publishedByUserId),
                    ("PublishedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var affected = await commandExecutor.ExecuteAsync(
                ReportingDefinitionSql.PublishDefinition,
                ReportingSqlParameters.Create(
                    ("DefinitionId", definitionId),
                    ("LatestPublishedVersionNumber", versionNumber),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConflictVersion();
        }

        return await queries.GetVersionAsync(definitionId, versionNumber, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
                ReportingDefinitionSql.FindDefinitionById,
                ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDelete();
        }

        await commandExecutor.ExecuteAsync(
                ReportingDefinitionSql.DeleteDefinitionVersions,
                ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                ReportingDefinitionSql.DeleteDefinition,
                ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private async Task<Result<DraftValidationResult>> ValidateDraftAsync(
        Guid groupId,
        Guid dataSourceId,
        string definitionKey,
        string name,
        string queryPortKey,
        IReadOnlyList<ReportingParameterSchemaEntry> parameterSchema,
        string? layoutConfigJson,
        Guid? definitionId,
        CancellationToken cancellationToken)
    {
        if (!ReportingDefinitionKeyValidator.IsValid(definitionKey))
        {
            return InvalidDraft<DraftValidationResult>("Definition key format is invalid.");
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrEmpty(normalizedName) || normalizedName.Length > 128)
        {
            return InvalidDraft<DraftValidationResult>("Name is required and must not exceed 128 characters.");
        }

        var group = await queryExecutor.QuerySingleOrDefaultAsync<ReportingGroupRecord>(
                ReportingGroupSql.FindById,
                ReportingSqlParameters.Create(("GroupId", groupId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (group is null || !group.IsEnabled)
        {
            return InvalidDraft<DraftValidationResult>("The reporting group was not found or is disabled.");
        }

        var dataSource = await dataSourceQueries.GetByIdAsync(dataSourceId, cancellationToken)
            .ConfigureAwait(false);
        if (!dataSource.IsSuccess || dataSource.Value is null || !dataSource.Value.IsEnabled)
        {
            return InvalidDraft<DraftValidationResult>("The reporting data source was not found or is disabled.");
        }

        var queryPort = ReportingQueryPortCatalog.TryGet(queryPortKey?.Trim() ?? string.Empty);
        if (queryPort is null)
        {
            return Result<DraftValidationResult>.Failure(new Error(
                ReportingErrorCodes.QueryPortNotFound,
                "The selected query port was not found.",
                ErrorType.Validation));
        }

        if (!queryPort.SupportedProviderKeys.Contains(dataSource.Value.ProviderKey, StringComparer.Ordinal))
        {
            return InvalidDraft<DraftValidationResult>("The selected query port does not support the data source provider.");
        }

        var schemaError = ReportingParameterSchemaValidator.Validate(queryPort, parameterSchema);
        if (schemaError is not null)
        {
            return Result<DraftValidationResult>.Failure(new Error(
                ReportingErrorCodes.ParameterSchemaInvalid,
                schemaError,
                ErrorType.Validation));
        }

        if (ReportingQueryPortCatalog.ResolveSql(queryPort.QueryPortKey, dataSource.Value.ProviderKey) is null)
        {
            return InvalidDraft<DraftValidationResult>("The selected query port cannot be resolved for the data source provider.");
        }

        if (!definitionId.HasValue)
        {
            var existing = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
                    ReportingDefinitionSql.FindDefinitionByKey,
                    ReportingSqlParameters.Create(("DefinitionKey", definitionKey.Trim())),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return Result<DraftValidationResult>.Failure(new Error(
                    ReportingErrorCodes.DefinitionKeyConflict,
                    "The reporting definition key already exists.",
                    ErrorType.Conflict));
            }
        }

        try
        {
            var normalizedLayout = ReportingDefinitionJson.NormalizeLayoutConfig(layoutConfigJson);
            return Result<DraftValidationResult>.Success(new DraftValidationResult(normalizedLayout));
        }
        catch (System.Text.Json.JsonException)
        {
            return InvalidDraft<DraftValidationResult>("Layout config must be valid JSON object.");
        }
    }

    private sealed record DraftValidationResult(string LayoutConfigJson);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<T> InvalidDraft<T>(string message) =>
        Result<T>.Failure(new Error(
            ReportingErrorCodes.DefinitionInvalid,
            message,
            ErrorType.Validation));

    private static Result<ReportingDefinitionResponse> NotFoundDefinition() =>
        Result<ReportingDefinitionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionNotFound,
            "The reporting definition was not found.",
            ErrorType.NotFound));

    private static Result<ReportingDefinitionResponse> ConflictDefinition() =>
        Result<ReportingDefinitionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionConcurrencyConflict,
            "The reporting definition was modified by another request.",
            ErrorType.Conflict));

    private static Result<ReportingDefinitionVersionResponse> NotFoundVersion() =>
        Result<ReportingDefinitionVersionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionNotFound,
            "The reporting definition was not found.",
            ErrorType.NotFound));

    private static Result<ReportingDefinitionVersionResponse> ConflictVersion() =>
        Result<ReportingDefinitionVersionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionConcurrencyConflict,
            "The reporting definition was modified by another request.",
            ErrorType.Conflict));

    private static Result<ReportingDefinitionVersionResponse> InvalidVersion(string message) =>
        Result<ReportingDefinitionVersionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionInvalid,
            message,
            ErrorType.Validation));

    private static Result<bool> NotFoundDelete() =>
        Result<bool>.Failure(new Error(
            ReportingErrorCodes.DefinitionNotFound,
            "The reporting definition was not found.",
            ErrorType.NotFound));
}
