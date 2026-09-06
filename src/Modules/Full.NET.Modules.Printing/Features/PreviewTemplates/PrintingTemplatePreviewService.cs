using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using Full.NET.Modules.Printing.Features.ManageTemplates;

namespace Full.NET.Modules.Printing.Features.PreviewTemplates;

/// <summary>对已发布打印模板执行数据绑定并生成预览 HTML。</summary>
internal sealed class PrintingTemplatePreviewService(
    PrintingTemplateQueryService templateQueries,
    PrintingFormBindingService bindingService,
    IClock clock)
{
    /// <summary>渲染指定发布版本的浏览器可打印 HTML。</summary>
    public async Task<Result<PrintingTemplatePreviewResponse>> PreviewAsync(
        Guid templateId,
        PreviewPrintingTemplateRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var templateResult = await templateQueries.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
        if (!templateResult.IsSuccess || templateResult.Value is null)
        {
            return Result<PrintingTemplatePreviewResponse>.Failure(templateResult.Error!);
        }

        var template = templateResult.Value;
        if (!template.IsEnabled)
        {
            return InvalidTemplate("The printing template is disabled.");
        }

        var versionNumber = request.VersionNumber ?? template.LatestPublishedVersionNumber;
        if (versionNumber <= 0)
        {
            return Result<PrintingTemplatePreviewResponse>.Failure(new Error(
                PrintingErrorCodes.TemplateNotPublished,
                "The printing template has no published version to preview.",
                ErrorType.Validation));
        }

        var versionResult = await templateQueries
            .GetVersionAsync(templateId, versionNumber, cancellationToken)
            .ConfigureAwait(false);
        if (!versionResult.IsSuccess || versionResult.Value is null)
        {
            return Result<PrintingTemplatePreviewResponse>.Failure(versionResult.Error!);
        }

        var bindingResult = await bindingService
            .ResolveAsync(template.FormSchemaKey, principal, cancellationToken)
            .ConfigureAwait(false);
        if (!bindingResult.IsSuccess || bindingResult.Value is null)
        {
            return Result<PrintingTemplatePreviewResponse>.Failure(bindingResult.Error!);
        }

        var html = PrintingHtmlRenderer.Render(versionResult.Value.LayoutHtml, bindingResult.Value);
        return Result<PrintingTemplatePreviewResponse>.Success(new PrintingTemplatePreviewResponse(
            template.Id,
            template.TemplateKey,
            template.Name,
            versionNumber,
            template.FormSchemaKey,
            html,
            bindingResult.Value,
            clock.UtcNow));
    }

    private static Result<PrintingTemplatePreviewResponse> InvalidTemplate(string message) =>
        Result<PrintingTemplatePreviewResponse>.Failure(new Error(
            PrintingErrorCodes.TemplateInvalid,
            message,
            ErrorType.Validation));
}
