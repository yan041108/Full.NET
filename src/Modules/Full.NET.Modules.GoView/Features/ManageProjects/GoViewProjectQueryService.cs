using Microsoft.Extensions.Options;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.GoView.Persistence;

namespace Full.NET.Modules.GoView.Features.ManageProjects;

/// <summary>GoView 大屏项目只读查询。</summary>
internal sealed class GoViewProjectQueryService(IQueryExecutor queryExecutor, IOptions<DatabaseOptions>? databaseOptions = null)
{
    public async Task<Result<GoViewProjectResponse>> GetByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<GoViewProjectRecord>(
                GoViewProjectSql.FindProjectById,
                GoViewSqlParameters.Create([("ProjectId", projectId)]),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFoundProject()
            : Result<GoViewProjectResponse>.Success(GoViewProjectMapper.MapProject(row));
    }

    public async Task<Result<PagedResult<GoViewProjectSummaryResponse>>> ListAsync(
        string? nameContains,
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var filter = NormalizeFilter(nameContains);
        var parameters = GoViewSqlParameters.Create([("NameContains", filter is null ? null : $"%{filter}%"),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize)]);
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(GoViewProjectSql.CountProjects,
            parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<GoViewProjectRecord>(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql ? GoViewProjectSql.ListProjectSummariesMySql : GoViewProjectSql.ListProjectSummariesSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<GoViewProjectSummaryResponse>>.Success(
            new PagedResult<GoViewProjectSummaryResponse>(rows.Select(row => new GoViewProjectSummaryResponse(row.Id, row.ProjectKey, row.Name, row.LatestPublishedVersionNumber, row.IsEnabled, row.CreatedAtUtc, row.UpdatedAtUtc, row.Version)).ToArray(), page, pageSize, total));
    }

    public async Task<Result<PagedResult<GoViewProjectVersionSummaryResponse>>> ListVersionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = GoViewSqlParameters.Create([("ProjectId", projectId),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize)]);
        if (await queryExecutor.QuerySingleOrDefaultAsync<long>(GoViewProjectSql.ProjectExists,
            parameters, cancellationToken).ConfigureAwait(false) == 0) return NotFoundVersions();
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(GoViewProjectSql.CountVersions,
            parameters, cancellationToken).ConfigureAwait(false);

        var rows = await queryExecutor.QueryAsync<GoViewProjectVersionRecord>(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql ? GoViewProjectSql.ListVersionSummariesMySql : GoViewProjectSql.ListVersionSummariesSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<GoViewProjectVersionSummaryResponse>>.Success(
            new PagedResult<GoViewProjectVersionSummaryResponse>(rows.Select(row => new GoViewProjectVersionSummaryResponse(row.Id, row.ProjectId, row.VersionNumber, row.ChangeNote, row.PublishedByUserId, row.PublishedAtUtc)).ToArray(), page, pageSize, total));
    }

    public async Task<Result<GoViewProjectVersionResponse>> GetVersionAsync(
        Guid projectId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<GoViewProjectVersionRecord>(
                GoViewProjectSql.FindVersionByNumber,
                GoViewSqlParameters.Create([
                    ("ProjectId", projectId),
                    ("VersionNumber", versionNumber)]),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFoundVersion()
            : Result<GoViewProjectVersionResponse>.Success(GoViewProjectMapper.MapVersion(row));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<GoViewProjectResponse> NotFoundProject() =>
        Result<GoViewProjectResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectNotFound,
            "The GoView project was not found.",
            ErrorType.NotFound));

    private static Result<PagedResult<GoViewProjectVersionSummaryResponse>> NotFoundVersions() =>
        Result<PagedResult<GoViewProjectVersionSummaryResponse>>.Failure(new Error(
            GoViewErrorCodes.ProjectNotFound,
            "The GoView project was not found.",
            ErrorType.NotFound));

    private static Result<GoViewProjectVersionResponse> NotFoundVersion() =>
        Result<GoViewProjectVersionResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectVersionNotFound,
            "The GoView project version was not found.",
            ErrorType.NotFound));
}
