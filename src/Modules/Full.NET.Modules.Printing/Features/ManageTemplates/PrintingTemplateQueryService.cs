using Microsoft.Extensions.Options;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Persistence;

namespace Full.NET.Modules.Printing.Features.ManageTemplates;

/// <summary>打印模板只读查询。</summary>
internal sealed class PrintingTemplateQueryService(IQueryExecutor queryExecutor, IOptions<DatabaseOptions>? databaseOptions = null)
{
    public async Task<Result<PrintingTemplateResponse>> GetByIdAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(
                PrintingTemplateSql.FindTemplateById,
                PrintingSqlParameters.Create([("TemplateId", templateId)]),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFoundTemplate()
            : Result<PrintingTemplateResponse>.Success(PrintingTemplateMapper.MapTemplate(row));
    }

    public async Task<Result<PagedResult<PrintingTemplateSummaryResponse>>> ListAsync(
        string? nameContains,
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var filter = NormalizeFilter(nameContains);
        var parameters = PrintingSqlParameters.Create([("NameContains", filter is null ? null : $"%{filter}%"),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize)]);
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(PrintingTemplateSql.CountTemplates,
            parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<PrintingTemplateRecord>(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql ? PrintingTemplateSql.ListTemplateSummariesMySql : PrintingTemplateSql.ListTemplateSummariesSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<PrintingTemplateSummaryResponse>>.Success(
            new PagedResult<PrintingTemplateSummaryResponse>(rows.Select(row => new PrintingTemplateSummaryResponse(row.Id, row.TemplateKey, row.Name, row.FormSchemaKey, row.LatestPublishedVersionNumber, row.IsEnabled, row.CreatedAtUtc, row.UpdatedAtUtc, row.Version)).ToArray(), page, pageSize, total));
    }

    public async Task<Result<PagedResult<PrintingTemplateVersionSummaryResponse>>> ListVersionsAsync(
        Guid templateId,
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = PrintingSqlParameters.Create([("TemplateId", templateId),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize)]);
        if (await queryExecutor.QuerySingleOrDefaultAsync<long>(PrintingTemplateSql.TemplateExists,
            parameters, cancellationToken).ConfigureAwait(false) == 0) return NotFoundVersions();
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(PrintingTemplateSql.CountVersions,
            parameters, cancellationToken).ConfigureAwait(false);

        var rows = await queryExecutor.QueryAsync<PrintingTemplateVersionRecord>(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql ? PrintingTemplateSql.ListVersionSummariesMySql : PrintingTemplateSql.ListVersionSummariesSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<PrintingTemplateVersionSummaryResponse>>.Success(
            new PagedResult<PrintingTemplateVersionSummaryResponse>(rows.Select(row => new PrintingTemplateVersionSummaryResponse(row.Id, row.TemplateId, row.VersionNumber, row.ChangeNote, row.PublishedByUserId, row.PublishedAtUtc)).ToArray(), page, pageSize, total));
    }

    public async Task<Result<PrintingTemplateVersionResponse>> GetVersionAsync(
        Guid templateId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<PrintingTemplateVersionRecord>(
                PrintingTemplateSql.FindVersionByNumber,
                PrintingSqlParameters.Create([
                    ("TemplateId", templateId),
                    ("VersionNumber", versionNumber)]),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFoundVersion()
            : Result<PrintingTemplateVersionResponse>.Success(PrintingTemplateMapper.MapVersion(row));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<PrintingTemplateResponse> NotFoundTemplate() =>
        Result<PrintingTemplateResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateNotFound,
            "The Printing template was not found.",
            ErrorType.NotFound));

    private static Result<PagedResult<PrintingTemplateVersionSummaryResponse>> NotFoundVersions() =>
        Result<PagedResult<PrintingTemplateVersionSummaryResponse>>.Failure(new Error(
            PrintingErrorCodes.TemplateNotFound,
            "The Printing template was not found.",
            ErrorType.NotFound));

    private static Result<PrintingTemplateVersionResponse> NotFoundVersion() =>
        Result<PrintingTemplateVersionResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateVersionNotFound,
            "The Printing template version was not found.",
            ErrorType.NotFound));
}
