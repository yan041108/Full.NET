using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using Full.NET.Modules.Printing.Persistence;

namespace Full.NET.Modules.Printing.Features.ManageTemplates;

/// <summary>打印模板草稿维护与发布版本。</summary>
internal sealed class PrintingTemplateManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PrintingTemplateQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<PrintingTemplateResponse>> CreateAsync(
        CreatePrintingTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => CreateCoreAsync(request, token), cancellationToken);

    public Task<Result<PrintingTemplateResponse>> UpdateAsync(
        Guid templateId,
        UpdatePrintingTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => UpdateCoreAsync(templateId, request, token), cancellationToken);

    public Task<Result<PrintingTemplateVersionResponse>> PublishAsync(
        Guid templateId,
        Guid publishedByUserId,
        PublishPrintingTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => PublishCoreAsync(templateId, publishedByUserId, request, token),
            cancellationToken);

    private async Task<Result<PrintingTemplateResponse>> CreateCoreAsync(
        CreatePrintingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateDraft(request.TemplateKey, request.Name, request.FormSchemaKey, request.LayoutHtml);
        if (!validation.IsSuccess)
        {
            return Result<PrintingTemplateResponse>.Failure(validation.Error!);
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(
                PrintingTemplateSql.FindTemplateByKey,
                PrintingSqlParameters.Create([("TemplateKey", request.TemplateKey.Trim())]),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<PrintingTemplateResponse>.Failure(new Error(
                PrintingErrorCodes.TemplateKeyConflict,
                "The printing template key already exists.",
                ErrorType.Conflict));
        }

        var templateId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                PrintingTemplateSql.InsertTemplate,
                PrintingSqlParameters.Create([
                    ("Id", templateId),
                    ("TemplateKey", request.TemplateKey.Trim()),
                    ("Name", request.Name.Trim()),
                    ("FormSchemaKey", request.FormSchemaKey.Trim()),
                    ("LayoutHtml", PrintingHtmlRenderer.SanitizeLayoutHtml(request.LayoutHtml)),
                    ("LatestPublishedVersionNumber", 0),
                    ("IsEnabled", request.IsEnabled),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)]),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PrintingTemplateResponse>> UpdateCoreAsync(
        Guid templateId,
        UpdatePrintingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var layoutValidation = ValidateLayoutHtml(request.LayoutHtml);
        if (!layoutValidation.IsSuccess)
        {
            return Result<PrintingTemplateResponse>.Failure(layoutValidation.Error!);
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(
                PrintingTemplateSql.FindTemplateById,
                PrintingSqlParameters.Create([("TemplateId", templateId)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFoundTemplate();
        }

        if (record.Version != request.Version)
        {
            return VersionConflict();
        }

        var affected = await commandExecutor.ExecuteAsync(
                PrintingTemplateSql.UpdateTemplate,
                PrintingSqlParameters.Create([
                    ("Id", templateId),
                    ("Name", request.Name.Trim()),
                    ("LayoutHtml", PrintingHtmlRenderer.SanitizeLayoutHtml(request.LayoutHtml)),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PrintingTemplateVersionResponse>> PublishCoreAsync(
        Guid templateId,
        Guid publishedByUserId,
        PublishPrintingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(
                PrintingTemplateSql.FindTemplateById,
                PrintingSqlParameters.Create([("TemplateId", templateId)]),
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

        if (PrintingFormSchemaCatalog.TryGet(record.FormSchemaKey) is null)
        {
            return Result<PrintingTemplateVersionResponse>.Failure(new Error(
                PrintingErrorCodes.TemplateInvalid,
                "The printing form schema is invalid.",
                ErrorType.Validation));
        }

        var layoutValidation = ValidateLayoutHtml(record.LayoutHtml);
        if (!layoutValidation.IsSuccess)
        {
            return Result<PrintingTemplateVersionResponse>.Failure(layoutValidation.Error!);
        }

        var versionNumber = record.LatestPublishedVersionNumber + 1;
        var versionId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                PrintingTemplateSql.InsertVersion,
                PrintingSqlParameters.Create([
                    ("Id", versionId),
                    ("TemplateId", templateId),
                    ("VersionNumber", versionNumber),
                    ("LayoutHtml", record.LayoutHtml),
                    ("ChangeNote", NormalizeOptional(request.ChangeNote)),
                    ("PublishedByUserId", publishedByUserId),
                    ("PublishedAtUtc", now)]),
                cancellationToken)
            .ConfigureAwait(false);

        var affected = await commandExecutor.ExecuteAsync(
                PrintingTemplateSql.PublishTemplate,
                PrintingSqlParameters.Create([
                    ("TemplateId", templateId),
                    ("LatestPublishedVersionNumber", versionNumber),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return VersionConflictVersion();
        }

        return await queries.GetVersionAsync(templateId, versionNumber, cancellationToken).ConfigureAwait(false);
    }

    private static Result<bool> ValidateDraft(
        string templateKey,
        string name,
        string formSchemaKey,
        string layoutHtml)
    {
        if (string.IsNullOrWhiteSpace(templateKey) || string.IsNullOrWhiteSpace(name))
        {
            return InvalidTemplate("Template key and name are required.");
        }

        if (PrintingFormSchemaCatalog.TryGet(formSchemaKey) is null)
        {
            return InvalidTemplate("The printing form schema was not found.");
        }

        return ValidateLayoutHtml(layoutHtml);
    }

    private static Result<bool> ValidateLayoutHtml(string layoutHtml)
    {
        if (string.IsNullOrWhiteSpace(layoutHtml))
        {
            return InvalidTemplate("Layout HTML is required.");
        }

        if (layoutHtml.Length > PrintingLayoutPolicy.MaxLayoutHtmlLength)
        {
            return Result<bool>.Failure(new Error(
                PrintingErrorCodes.LayoutHtmlInvalid,
                "Layout HTML exceeds the maximum allowed length.",
                ErrorType.Validation));
        }

        return Result<bool>.Success(true);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<PrintingTemplateResponse> NotFoundTemplate() =>
        Result<PrintingTemplateResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateNotFound,
            "The printing template was not found.",
            ErrorType.NotFound));

    private static Result<PrintingTemplateVersionResponse> NotFoundVersion() =>
        Result<PrintingTemplateVersionResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateNotFound,
            "The printing template was not found.",
            ErrorType.NotFound));

    private static Result<PrintingTemplateResponse> VersionConflict() =>
        Result<PrintingTemplateResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateConcurrencyConflict,
            "The printing template was modified by another request.",
            ErrorType.Conflict));

    private static Result<PrintingTemplateVersionResponse> VersionConflictVersion() =>
        Result<PrintingTemplateVersionResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateConcurrencyConflict,
            "The printing template was modified by another request.",
            ErrorType.Conflict));

    private static Result<bool> InvalidTemplate(string message) =>
        Result<bool>.Failure(new Error(
            PrintingErrorCodes.TemplateInvalid,
            message,
            ErrorType.Validation));
}
