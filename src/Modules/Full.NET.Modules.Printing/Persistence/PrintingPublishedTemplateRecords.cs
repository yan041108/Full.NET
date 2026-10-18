namespace Full.NET.Modules.Printing.Persistence;

/// <summary>获授版本目录的最小数据库投影。</summary>
internal class PrintingPublishedTemplateRecord
{
    public Guid TemplateId { get; set; }
    public string TemplateKey { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string FormSchemaKey { get; set; } = "";
    public int VersionNumber { get; set; }
}

/// <summary>预览只加载已经通过当前租户精确授权过滤的不可变布局。</summary>
internal sealed class PrintingGrantedVersionRecord : PrintingPublishedTemplateRecord
{
    public string LayoutHtml { get; set; } = "";
}
