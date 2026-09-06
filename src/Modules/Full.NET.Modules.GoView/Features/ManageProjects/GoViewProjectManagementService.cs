using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.GoView.Domain;
using Full.NET.Modules.GoView.Persistence;

namespace Full.NET.Modules.GoView.Features.ManageProjects;

/// <summary>GoView 大屏项目草稿维护与发布版本。</summary>
internal sealed class GoViewProjectManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    GoViewProjectQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<GoViewProjectResponse>> CreateAsync(
        CreateGoViewProjectRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => CreateCoreAsync(request, token), cancellationToken);

    public Task<Result<GoViewProjectResponse>> UpdateAsync(
        Guid projectId,
        UpdateGoViewProjectRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => UpdateCoreAsync(projectId, request, token), cancellationToken);

    public Task<Result<GoViewProjectVersionResponse>> PublishAsync(
        Guid projectId,
        Guid publishedByUserId,
        PublishGoViewProjectRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => PublishCoreAsync(projectId, publishedByUserId, request, token),
            cancellationToken);

    private async Task<Result<GoViewProjectResponse>> CreateCoreAsync(
        CreateGoViewProjectRequest request,
        CancellationToken cancellationToken)
    {
        var canvasJson = GoViewCanvasValidator.NormalizeOptional(request.CanvasJson);
        var validation = ValidateDraft(request.ProjectKey, request.Name, canvasJson);
        if (!validation.IsSuccess)
        {
            return Result<GoViewProjectResponse>.Failure(validation.Error!);
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<GoViewProjectRecord>(
                GoViewProjectSql.FindProjectByKey,
                GoViewSqlParameters.Create([("ProjectKey", request.ProjectKey.Trim())]),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<GoViewProjectResponse>.Failure(new Error(
                GoViewErrorCodes.ProjectKeyConflict,
                "The GoView project key already exists.",
                ErrorType.Conflict));
        }

        var projectId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                GoViewProjectSql.InsertProject,
                GoViewSqlParameters.Create([
                    ("Id", projectId),
                    ("ProjectKey", request.ProjectKey.Trim()),
                    ("Name", request.Name.Trim()),
                    ("CanvasJson", canvasJson),
                    ("LatestPublishedVersionNumber", 0),
                    ("IsEnabled", request.IsEnabled),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)]),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<GoViewProjectResponse>> UpdateCoreAsync(
        Guid projectId,
        UpdateGoViewProjectRequest request,
        CancellationToken cancellationToken)
    {
        var canvasValidation = GoViewCanvasValidator.Validate(request.CanvasJson);
        if (!canvasValidation.IsSuccess)
        {
            return Result<GoViewProjectResponse>.Failure(canvasValidation.Error!);
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<GoViewProjectRecord>(
                GoViewProjectSql.FindProjectById,
                GoViewSqlParameters.Create([("ProjectId", projectId)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFoundProject();
        }

        if (record.Version != request.Version)
        {
            return VersionConflict();
        }

        var affected = await commandExecutor.ExecuteAsync(
                GoViewProjectSql.UpdateProject,
                GoViewSqlParameters.Create([
                    ("Id", projectId),
                    ("Name", request.Name.Trim()),
                    ("CanvasJson", request.CanvasJson.Trim()),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<GoViewProjectVersionResponse>> PublishCoreAsync(
        Guid projectId,
        Guid publishedByUserId,
        PublishGoViewProjectRequest request,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<GoViewProjectRecord>(
                GoViewProjectSql.FindProjectById,
                GoViewSqlParameters.Create([("ProjectId", projectId)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFoundVersion();
        }

        if (record.Version != request.Version)
        {
            return VersionConflictVersion();
        }

        var canvasValidation = GoViewCanvasValidator.Validate(record.CanvasJson);
        if (!canvasValidation.IsSuccess)
        {
            return Result<GoViewProjectVersionResponse>.Failure(canvasValidation.Error!);
        }

        var versionNumber = record.LatestPublishedVersionNumber + 1;
        var versionId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                GoViewProjectSql.InsertVersion,
                GoViewSqlParameters.Create([
                    ("Id", versionId),
                    ("ProjectId", projectId),
                    ("VersionNumber", versionNumber),
                    ("CanvasJson", record.CanvasJson),
                    ("ChangeNote", NormalizeOptional(request.ChangeNote)),
                    ("PublishedByUserId", publishedByUserId),
                    ("PublishedAtUtc", now)]),
                cancellationToken)
            .ConfigureAwait(false);

        var affected = await commandExecutor.ExecuteAsync(
                GoViewProjectSql.PublishProject,
                GoViewSqlParameters.Create([
                    ("ProjectId", projectId),
                    ("LatestPublishedVersionNumber", versionNumber),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return VersionConflictVersion();
        }

        return await queries.GetVersionAsync(projectId, versionNumber, cancellationToken).ConfigureAwait(false);
    }

    private static Result<bool> ValidateDraft(string projectKey, string name, string canvasJson)
    {
        if (string.IsNullOrWhiteSpace(projectKey) || string.IsNullOrWhiteSpace(name))
        {
            return InvalidProject("Project key and name are required.");
        }

        return GoViewCanvasValidator.Validate(canvasJson);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<GoViewProjectResponse> NotFoundProject() =>
        Result<GoViewProjectResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectNotFound,
            "The GoView project was not found.",
            ErrorType.NotFound));

    private static Result<GoViewProjectVersionResponse> NotFoundVersion() =>
        Result<GoViewProjectVersionResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectNotFound,
            "The GoView project was not found.",
            ErrorType.NotFound));

    private static Result<GoViewProjectResponse> VersionConflict() =>
        Result<GoViewProjectResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectConcurrencyConflict,
            "The GoView project was modified by another request.",
            ErrorType.Conflict));

    private static Result<GoViewProjectVersionResponse> VersionConflictVersion() =>
        Result<GoViewProjectVersionResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectConcurrencyConflict,
            "The GoView project was modified by another request.",
            ErrorType.Conflict));

    private static Result<bool> InvalidProject(string message) =>
        Result<bool>.Failure(new Error(
            GoViewErrorCodes.ProjectInvalid,
            message,
            ErrorType.Validation));
}
