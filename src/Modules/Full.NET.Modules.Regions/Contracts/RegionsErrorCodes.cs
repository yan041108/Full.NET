namespace Full.NET.Modules.Regions.Contracts;

/// <summary>行政区域模块稳定错误码前缀与具体错误键。</summary>
public static class RegionsErrorCodes
{
    /// <summary>错误码前缀，用于资源文件分组。</summary>
    public const string Prefix = "regions.";

    /// <summary>请求校验失败。</summary>
    public const string ValidationFailed = "regions.administrative_region_validation_failed";

    /// <summary>行政区域不存在。</summary>
    public const string NotFound = "regions.administrative_region_not_found";

    /// <summary>乐观并发冲突。</summary>
    public const string ConcurrencyConflict = "regions.administrative_region_concurrency_conflict";

    /// <summary>区域编码已存在。</summary>
    public const string CodeExists = "regions.administrative_region_code_exists";

    /// <summary>区域编码格式无效。</summary>
    public const string InvalidCode = "regions.administrative_region_invalid_code";

    /// <summary>父级区域无效。</summary>
    public const string InvalidParent = "regions.administrative_region_invalid_parent";

    /// <summary>父子关系会形成环。</summary>
    public const string ParentCycle = "regions.administrative_region_parent_cycle";

    /// <summary>导入请求无效。</summary>
    public const string ImportInvalid = "regions.administrative_region_import_invalid";

    /// <summary>导入父级编码无法解析。</summary>
    public const string ImportParentUnresolved = "regions.administrative_region_import_parent_unresolved";
}
