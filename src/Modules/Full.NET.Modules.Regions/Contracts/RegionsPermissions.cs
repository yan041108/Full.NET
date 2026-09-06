namespace Full.NET.Modules.Regions.Contracts;

/// <summary>
/// 行政区域相关操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class RegionsPermissions
{
    /// <summary>允许查询行政区域（含级联读取与树查询）。</summary>
    public const string Read = "regions.administrative_regions.read";

    /// <summary>允许创建行政区域节点。</summary>
    public const string Create = "regions.administrative_regions.create";

    /// <summary>允许更新行政区域节点。</summary>
    public const string Update = "regions.administrative_regions.update";

    /// <summary>允许删除行政区域节点及其子孙节点。</summary>
    public const string Delete = "regions.administrative_regions.delete";

    /// <summary>允许预览与应用行政区域数据集导入。</summary>
    public const string Import = "regions.administrative_regions.import";
}
