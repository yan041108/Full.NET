using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using Full.NET.Modules.Printing.Features.PreviewTemplates;
using Full.NET.Modules.Printing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Printing.Features.PublishedTemplates;

/// <summary>租户只读获授发布快照；绑定期间撤权后不得返回已渲染内容。</summary>
internal sealed class PrintingPublishedTemplateService(
    ICurrentTenant tenant, IQueryExecutor queries, IOptions<DatabaseOptions> database,
    PrintingFormBindingService bindings, IClock clock, PrintingFormSchemaCatalog catalog)
{
    /// <summary>只返回当前可信租户获授且仍启用的版本目录；Host 上下文拒绝读取。</summary>
    public async Task<Result<IReadOnlyList<PrintingPublishedTemplateResponse>>> ListAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (tenant.IsHost || tenant.Id is null)
            return Denied<IReadOnlyList<PrintingPublishedTemplateResponse>>();
        var rows = await queries.QueryAsync<PrintingPublishedTemplateRecord>(PrintingTenantGrantSql.ListPublished,
            cancellationToken: token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Result<IReadOnlyList<PrintingPublishedTemplateResponse>>.Success(rows.Select(row =>
            new PrintingPublishedTemplateResponse(row.TemplateId, row.TemplateKey, row.TemplateName,
                row.FormSchemaKey, row.VersionNumber, catalog.TryGet(row.FormSchemaKey)?.RequiresRecordId ?? false)).ToArray());
    }

    /// <summary>读取获授快照并绑定当前租户，返回前复核同一版本的实时授权与启用状态。</summary>
    /// <param name="request">缺省版本只选择租户已获授的最高版本。</param>
    public async Task<Result<PrintingTemplatePreviewResponse>> PreviewAsync(Guid templateId,
        PreviewPrintingTemplateRequest request, ClaimsPrincipal principal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (tenant.IsHost || tenant.Id is null || templateId == Guid.Empty || request.VersionNumber is <= 0)
            return Denied<PrintingTemplatePreviewResponse>();
        var row = await ResolveAsync(templateId, request.VersionNumber, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (row is null) return Denied<PrintingTemplatePreviewResponse>();
        var binding = await bindings.ResolveAsync(row.FormSchemaKey, principal, token, request.RecordId).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!binding.IsSuccess || binding.Value is null)
            return Result<PrintingTemplatePreviewResponse>.Failure(binding.Error!);
        // 权威源复核精确版本，避免跨模块绑定期间撤权或停用后继续交付内容。
        var stillGranted = await ResolveAsync(templateId, row.VersionNumber, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (stillGranted is null) return Denied<PrintingTemplatePreviewResponse>();
        string html;
        try
        {
            html = PrintingHtmlRenderer.Render(row.LayoutHtml, binding.Value, token);
        }
        catch (PrintingRenderingLimitException exception)
        {
            token.ThrowIfCancellationRequested();
            return Result<PrintingTemplatePreviewResponse>.Failure(new Error(exception.Code,
                "The printing preview exceeds the supported rendering limits.", ErrorType.Validation));
        }
        return Result<PrintingTemplatePreviewResponse>.Success(new(row.TemplateId, row.TemplateKey,
            row.TemplateName, row.VersionNumber, row.FormSchemaKey,
            html, binding.Value, clock.UtcNow));
    }

    private Task<PrintingGrantedVersionRecord?> ResolveAsync(Guid templateId, int? version, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<PrintingGrantedVersionRecord>(PrintingTenantGrantSql.ResolveVersion(database.Value.Provider),
            PrintingSqlParameters.Create(("TemplateId", templateId), ("VersionNumber", version)), token);

    private static Result<T> Denied<T>() => Result<T>.Failure(new(CommonErrorCodes.PermissionDenied,
        "An enabled printing version granted to the current tenant is required.", ErrorType.Forbidden));
}
