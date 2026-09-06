namespace Full.NET.Modules.Printing.Contracts;

/// <summary>Printing 模块稳定业务错误码。</summary>
public static class PrintingErrorCodes
{
    /// <summary>打印模板不存在。</summary>
    public const string TemplateNotFound = "printing.template.not_found";

    /// <summary>打印模板元数据校验失败。</summary>
    public const string TemplateInvalid = "printing.template.invalid";

    /// <summary>打印模板键冲突。</summary>
    public const string TemplateKeyConflict = "printing.template.key_conflict";

    /// <summary>打印模板并发版本冲突。</summary>
    public const string TemplateConcurrencyConflict = "printing.template.concurrency_conflict";

    /// <summary>固定表单 Schema 不存在。</summary>
    public const string FormSchemaNotFound = "printing.form_schema.not_found";

    /// <summary>打印模板版本不存在。</summary>
    public const string TemplateVersionNotFound = "printing.template_version.not_found";

    /// <summary>打印模板尚未发布，无法预览。</summary>
    public const string TemplateNotPublished = "printing.template.not_published";

    /// <summary>打印布局 HTML 无效或超过大小上限。</summary>
    public const string LayoutHtmlInvalid = "printing.layout_html.invalid";

    /// <summary>打印数据绑定失败。</summary>
    public const string BindingFailed = "printing.binding.failed";
}
