namespace Full.NET.Modules.Printing.Contracts;

/// <summary>打印模板响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。TemplateKey、FormSchemaKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Id">模板标识。</param>
/// <param name="TemplateKey">模板稳定机器码；发布后不可改名或删除。</param>
/// <param name="Name">模板展示名称。</param>
/// <param name="FormSchemaKey">表单 Schema 稳定机器码；定位渲染入参。</param>
/// <param name="LayoutHtml">草稿 HTML 布局；不含已发布内容。</param>
/// <param name="LatestPublishedVersionNumber">最近一次发布版本号；未发布为 0。</param>
/// <param name="IsEnabled">模板是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；可为空。</param>
/// <param name="Version">乐观锁版本号。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。TemplateKey、FormSchemaKey 创建后不可变。</remarks>
/// <param name="TemplateKey">模板稳定机器码；发布后不可改名或删除。</param>
/// <param name="Name">模板展示名称。</param>
/// <param name="FormSchemaKey">表单 Schema 稳定机器码；定位渲染入参。</param>
/// <param name="LayoutHtml">草稿 HTML 布局。</param>
/// <param name="IsEnabled">模板是否启用。</param>
public sealed record CreatePrintingTemplateRequest(
    string TemplateKey,
    string Name,
    string FormSchemaKey,
    string LayoutHtml,
    bool IsEnabled);

/// <summary>更新打印模板草稿请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Name">模板展示名称。</param>
/// <param name="LayoutHtml">草稿 HTML 布局。</param>
/// <param name="IsEnabled">模板是否启用。</param>
/// <param name="Version">乐观锁版本号，用于 CAS 并发控制。</param>
public sealed record UpdatePrintingTemplateRequest(
    string Name,
    string LayoutHtml,
    bool IsEnabled,
    int Version);

/// <summary>发布打印模板版本请求。</summary>
/// <param name="ChangeNote">版本变更说明；可为空。</param>
/// <param name="Version">乐观锁版本号，用于 CAS 并发控制。</param>
public sealed record PublishPrintingTemplateRequest(
    string? ChangeNote,
    int Version);

/// <summary>打印模板版本响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">版本标识。</param>
/// <param name="TemplateId">归属模板标识。</param>
/// <param name="VersionNumber">版本号；按发布顺序自增。</param>
/// <param name="LayoutHtml">该版本发布时的 HTML 布局快照。</param>
/// <param name="ChangeNote">版本变更说明；可为空。</param>
/// <param name="PublishedByUserId">发布用户标识。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）。</param>
public sealed record PrintingTemplateVersionResponse(
    Guid Id,
    Guid TemplateId,
    int VersionNumber,
    string LayoutHtml,
    string? ChangeNote,
    Guid PublishedByUserId,
    DateTimeOffset PublishedAtUtc);

/// <summary>打印预览请求。</summary>
/// <param name="VersionNumber">指定预览版本号；为空使用最新发布版本。</param>
public sealed record PreviewPrintingTemplateRequest(
    int? VersionNumber);

/// <summary>打印预览响应；HTML 仅供浏览器预览/打印，不包含脚本。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。TemplateKey、FormSchemaKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="TemplateId">模板标识。</param>
/// <param name="TemplateKey">模板稳定机器码。</param>
/// <param name="TemplateName">模板展示名称。</param>
/// <param name="VersionNumber">预览版本号。</param>
/// <param name="FormSchemaKey">表单 Schema 稳定机器码。</param>
/// <param name="Html">渲染后的预览 HTML；不含脚本。</param>
/// <param name="BoundFields">字段名到绑定值的映射；键名来自 FormSchema。</param>
/// <param name="GeneratedAtUtc">预览生成时间（UTC）。</param>
public sealed record PrintingTemplatePreviewResponse(
    Guid TemplateId,
    string TemplateKey,
    string TemplateName,
    int VersionNumber,
    string FormSchemaKey,
    string Html,
    IReadOnlyDictionary<string, string?> BoundFields,
    DateTimeOffset GeneratedAtUtc);
