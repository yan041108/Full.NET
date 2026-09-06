using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.GoView.Persistence;

namespace Full.NET.Modules.GoView.Features.ManageProjects;

/// <summary>GoView 大屏项目只读查询。</summary>
internal sealed class GoViewProjectQueryService(IQueryExecutor queryExecutor)
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

    public async Task<Result<IReadOnlyList<GoViewProjectResponse>>> ListAsync(
        string? nameContains,
        CancellationToken cancellationToken = default)
    {
        var filter = NormalizeFilter(nameContains);
        var rows = await queryExecutor.QueryAsync<GoViewProjectRecord>(
                GoViewProjectSql.ListProjects,
                GoViewSqlParameters.Create([
                    ("NameContains", filter is null ? null : $"%{filter}%")]),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<GoViewProjectResponse>>.Success(
            rows.Select(GoViewProjectMapper.MapProject).ToArray());
    }

    public async Task<Result<IReadOnlyList<GoViewProjectVersionResponse>>> ListVersionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (await queryExecutor.QuerySingleOrDefaultAsync<GoViewProjectRecord>(
                    GoViewProjectSql.FindProjectById,
                    GoViewSqlParameters.Create([("ProjectId", projectId)]),
                    cancellationToken)
                .ConfigureAwait(false) is null)
        {
            return NotFoundVersions();
        }

        var rows = await queryExecutor.QueryAsync<GoViewProjectVersionRecord>(
                GoViewProjectSql.ListVersions,
                GoViewSqlParameters.Create([("ProjectId", projectId)]),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<GoViewProjectVersionResponse>>.Success(
            rows.Select(GoViewProjectMapper.MapVersion).ToArray());
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

    private static Result<IReadOnlyList<GoViewProjectVersionResponse>> NotFoundVersions() =>
        Result<IReadOnlyList<GoViewProjectVersionResponse>>.Failure(new Error(
            GoViewErrorCodes.ProjectNotFound,
            "The GoView project was not found.",
            ErrorType.NotFound));

    private static Result<GoViewProjectVersionResponse> NotFoundVersion() =>
        Result<GoViewProjectVersionResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectVersionNotFound,
            "The GoView project version was not found.",
            ErrorType.NotFound));
}
