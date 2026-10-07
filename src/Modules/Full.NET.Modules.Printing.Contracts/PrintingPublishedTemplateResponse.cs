namespace Full.NET.Modules.Printing.Contracts;

/// <summary>当前租户获授的精确发布版本元数据，不公开草稿布局和绑定数据。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只追加到末尾，不公开草稿或租户绑定值。</remarks>
/// <param name="TemplateId">打印模板标识。</param>
/// <param name="TemplateKey">稳定模板键。</param>
/// <param name="TemplateName">模板显示名称。</param>
/// <param name="FormSchemaKey">固定表单 Schema 键。</param>
/// <param name="VersionNumber">获授的不可变发布版本。</param>
public sealed record PrintingPublishedTemplateResponse(
    Guid TemplateId, string TemplateKey, string TemplateName, string FormSchemaKey, int VersionNumber);
