using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Persistence;

namespace Full.NET.Modules.Printing.Features.ManageTemplates;

/// <summary>打印模板只读查询。</summary>
internal sealed class PrintingTemplateQueryService(IQueryExecutor queryExecutor)
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

    public async Task<Result<IReadOnlyList<PrintingTemplateResponse>>> ListAsync(
        string? nameContains,
        CancellationToken cancellationToken = default)
    {
        var filter = NormalizeFilter(nameContains);
        var rows = await queryExecutor.QueryAsync<PrintingTemplateRecord>(
                PrintingTemplateSql.ListTemplates,
                PrintingSqlParameters.Create([
                    ("NameContains", filter is null ? null : $"%{filter}%")]),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<PrintingTemplateResponse>>.Success(
            rows.Select(PrintingTemplateMapper.MapTemplate).ToArray());
    }

    public async Task<Result<IReadOnlyList<PrintingTemplateVersionResponse>>> ListVersionsAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        if (await queryExecutor.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(
                    PrintingTemplateSql.FindTemplateById,
                    PrintingSqlParameters.Create([("TemplateId", templateId)]),
                    cancellationToken)
                .ConfigureAwait(false) is null)
        {
            return NotFoundVersions();
        }

        var rows = await queryExecutor.QueryAsync<PrintingTemplateVersionRecord>(
                PrintingTemplateSql.ListVersions,
                PrintingSqlParameters.Create([("TemplateId", templateId)]),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<PrintingTemplateVersionResponse>>.Success(
            rows.Select(PrintingTemplateMapper.MapVersion).ToArray());
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
            "The printing template was not found.",
            ErrorType.NotFound));

    private static Result<IReadOnlyList<PrintingTemplateVersionResponse>> NotFoundVersions() =>
        Result<IReadOnlyList<PrintingTemplateVersionResponse>>.Failure(new Error(
            PrintingErrorCodes.TemplateNotFound,
            "The printing template was not found.",
            ErrorType.NotFound));

    private static Result<PrintingTemplateVersionResponse> NotFoundVersion() =>
        Result<PrintingTemplateVersionResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateVersionNotFound,
            "The printing template version was not found.",
            ErrorType.NotFound));
}
