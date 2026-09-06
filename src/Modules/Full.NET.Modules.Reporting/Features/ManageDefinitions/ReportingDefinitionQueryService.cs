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

    public async Task<Result<IReadOnlyList<ReportingDefinitionResponse>>> ListAsync(
        Guid? groupId,
        string? nameContains,
        CancellationToken cancellationToken = default)
    {
        var filter = ReportingSqlParameters.Create(
            ("GroupId", groupId),
            ("NameContains", NormalizeFilter(nameContains)));
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ReportingDefinitionSql.ListDefinitions,
            DatabaseProvider.MySql => new SqlStatement(
                "reporting.list_definitions_mysql",
                ReportingDefinitionSql.ListDefinitionsMySql,
                SqlDataScope.HostOnly),
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<ReportingDefinitionRecord>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<ReportingDefinitionResponse>>.Success(
            rows.Select(ReportingDefinitionMapper.MapDefinition).ToArray());
    }

    public async Task<Result<IReadOnlyList<ReportingDefinitionVersionResponse>>> ListVersionsAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        if (await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
                    ReportingDefinitionSql.FindDefinitionById,
                    ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                    cancellationToken)
                .ConfigureAwait(false) is null)
        {
            return NotFoundVersions();
        }

        var rows = await queryExecutor.QueryAsync<ReportingDefinitionVersionRecord>(
                ReportingDefinitionSql.ListVersions,
                ReportingSqlParameters.Create(("DefinitionId", definitionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<ReportingDefinitionVersionResponse>>.Success(
            rows.Select(ReportingDefinitionMapper.MapVersion).ToArray());
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

    private static Result<IReadOnlyList<ReportingDefinitionVersionResponse>> NotFoundVersions() =>
        Result<IReadOnlyList<ReportingDefinitionVersionResponse>>.Failure(new Error(
            ReportingErrorCodes.DefinitionNotFound,
            "The reporting definition was not found.",
            ErrorType.NotFound));

    private static Result<ReportingDefinitionVersionResponse> NotFoundVersion() =>
        Result<ReportingDefinitionVersionResponse>.Failure(new Error(
            ReportingErrorCodes.DefinitionVersionNotFound,
            "The reporting definition version was not found.",
            ErrorType.NotFound));
}
