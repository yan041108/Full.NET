namespace Full.NET.Modules.Printing.Contracts;

/// <summary>打印模板响应。</summary>
public sealed record PrintingTemplateResponse(
    Guid Id,
    string TemplateKey,
    string Name,
    string FormSchemaKey,
    string LayoutHtml,
    int LatestPublishedVersionNumber,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建打印模板请求。</summary>
public sealed record CreatePrintingTemplateRequest(
    string TemplateKey,
    string Name,
    string FormSchemaKey,
    string LayoutHtml,
    bool IsEnabled);

/// <summary>更新打印模板草稿请求。</summary>
public sealed record UpdatePrintingTemplateRequest(
    string Name,
    string LayoutHtml,
    bool IsEnabled,
    int Version);

/// <summary>发布打印模板版本请求。</summary>
public sealed record PublishPrintingTemplateRequest(
    string? ChangeNote,
    int Version);

/// <summary>打印模板版本响应。</summary>
public sealed record PrintingTemplateVersionResponse(
    Guid Id,
    Guid TemplateId,
    int VersionNumber,
    string LayoutHtml,
    string? ChangeNote,
    Guid PublishedByUserId,
    DateTimeOffset PublishedAtUtc);

/// <summary>打印预览请求。</summary>
public sealed record PreviewPrintingTemplateRequest(
    int? VersionNumber);

/// <summary>打印预览响应；HTML 仅供浏览器预览/打印，不包含脚本。</summary>
public sealed record PrintingTemplatePreviewResponse(
    Guid TemplateId,
    string TemplateKey,
    string TemplateName,
    int VersionNumber,
    string FormSchemaKey,
    string Html,
    IReadOnlyDictionary<string, string?> BoundFields,
    DateTimeOffset GeneratedAtUtc);
