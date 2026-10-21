using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageDefinitions;

/// <summary>报表定义与发布版本只读查询。</summary>
internal sealed class ReportingDefinitionQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<ReportingDefinitionResponse>> GetByIdAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
                ReportingDefinitionSql.FindDefinitionById,
                ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFoundDefinition()
            : Result<ReportingDefinitionResponse>.Success(ReportingDefinitionMapper.MapDefinition(row));
    }

    public async Task<Result<PagedResult<ReportingDefinitionSummaryResponse>>> ListAsync(
        Guid? groupId, string? nameContains, CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var name = NormalizeFilter(nameContains);
        var parameters = ReportingSqlParameters.Create(("GroupId", groupId),
            ("NameContains", name is null ? null : $"%{name}%"), ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            ReportingDefinitionSql.CountSummaries, parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<ReportingDefinitionRecord>(
            databaseOptions.Value.Provider == DatabaseProvider.MySql
                ? ReportingDefinitionSql.ListSummariesMySql : ReportingDefinitionSql.ListSummariesSqlServer,
            parameters, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<ReportingDefinitionSummaryResponse>>.Success(new(
            rows.Select(row => new ReportingDefinitionSummaryResponse(row.Id, row.GroupId, row.DataSourceId, row.DefinitionKey,
                row.Name, row.Description, row.QueryPortKey, row.LatestPublishedVersionNumber, row.IsEnabled,
                row.CreatedAtUtc, row.UpdatedAtUtc, row.Version)).ToArray(), page, pageSize, total));
    }

    public async Task<Result<PagedResult<ReportingDefinitionVersionSummaryResponse>>> ListVersionsAsync(
        Guid definitionId, CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = ReportingSqlParameters.Create(("DefinitionId", definitionId),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        if (await queryExecutor.QuerySingleOrDefaultAsync<long>(ReportingDefinitionSql.DefinitionExists,
            parameters, cancellationToken).ConfigureAwait(false) == 0) return NotFoundVersions();
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            ReportingDefinitionSql.CountVersionSummaries, parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<ReportingDefinitionVersionRecord>(
            databaseOptions.Value.Provider == DatabaseProvider.MySql
                ? ReportingDefinitionSql.ListVersionSummariesMySql : ReportingDefinitionSql.ListVersionSummariesSqlServer,
            parameters, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<ReportingDefinitionVersionSummaryResponse>>.Success(new(
            rows.Select(row => new ReportingDefinitionVersionSummaryResponse(row.Id, row.DefinitionId, row.VersionNumber,
                row.DataSourceId, row.QueryPortKey, row.ChangeNote, row.PublishedByUserId, row.PublishedAtUtc)).ToArray(),
            page, pageSize, total));
    }

    public async Task<Result<ReportingDefinitionVersionResponse>> GetVersionAsync(
        Guid definitionId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(
                ReportingDefinitionSql.FindVersionByNumber,
                ReportingSqlParameters.Create(
                    ("DefinitionId", definitionId),
                    ("VersionNumber", versionNumber)),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFoundVersion()
            : Result<ReportingDefinitionVersionResponse>.Success(ReportingDefinitionMapper.MapVersion(row));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<ReportingDefinitionResponse> NotFoundDefinition() =>
        Result<ReportingDefinitionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionNotFound,
            "The reporting definition was not found.",
            ErrorType.NotFound));

    private static Result<PagedResult<ReportingDefinitionVersionSummaryResponse>> NotFoundVersions() =>
        Result<PagedResult<ReportingDefinitionVersionSummaryResponse>>.Failure(new Error(
            ReportingErrorCodes.DefinitionNotFound,
            "The reporting definition was not found.",
            ErrorType.NotFound));

    private static Result<ReportingDefinitionVersionResponse> NotFoundVersion() =>
        Result<ReportingDefinitionVersionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionVersionNotFound,
            "The reporting definition version was not found.",
            ErrorType.NotFound));
}
