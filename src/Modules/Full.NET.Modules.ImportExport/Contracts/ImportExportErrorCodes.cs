namespace Full.NET.Modules.ImportExport.Contracts;

/// <summary>ImportExport 模块对外返回的稳定错误码目录。</summary>
public static class ImportExportErrorCodes
{
    /// <summary>ImportExport 模块所有错误码的统一前缀。</summary>
    public const string Prefix = "import_export.";

    /// <summary>请求的静态 Schema 未注册或当前租户不可见。</summary>
    public const string SchemaNotFound = "import_export.schema.not_found";

    /// <summary>请求的工作表键不属于指定 Schema。</summary>
    public const string WorksheetNotFound = "import_export.worksheet.not_found";

    /// <summary>导入任务不存在或不属于当前租户。</summary>
    public const string TaskNotFound = "import_export.task.not_found";

    /// <summary>上传文件类型或结构不受支持。</summary>
    public const string FileInvalid = "import_export.file.invalid";

    /// <summary>上传文件超过配置的大小上限。</summary>
    public const string FileTooLarge = "import_export.file.too_large";

    /// <summary>工作表行数超过配置的上限。</summary>
    public const string RowLimitExceeded = "import_export.rows.limit_exceeded";

    /// <summary>预校验执行失败且无法产生行级结果。</summary>
    public const string PreviewFailed = "import_export.preview.failed";
}
