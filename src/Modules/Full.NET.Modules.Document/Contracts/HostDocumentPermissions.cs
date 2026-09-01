namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// 主机文档主资源稳定权限码；不可本地化，作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class HostDocumentPermissions
{
    /// <summary>允许读取主机文档列表、详情与版本列表。</summary>
    public const string Read = "document.host_documents.read";

    /// <summary>允许创建新的主机文档项，含草稿与已发布初始状态。</summary>
    public const string Create = "document.host_documents.create";

    /// <summary>允许修改主机文档的元数据（标题、描述、分类、标签、排序、缩略图等）。</summary>
    public const string Update = "document.host_documents.update";

    /// <summary>允许为已有文档追加新版本。</summary>
    public const string AddVersion = "document.host_documents.add_version";

    /// <summary>允许下载当前版本的实际文件；不含该权限的用户只能查看元数据。</summary>
    public const string Download = "document.host_documents.download";

    /// <summary>允许软删除主机文档，将其移入回收站。</summary>
    public const string Delete = "document.host_documents.delete";

    /// <summary>允许从回收站恢复软删除的主机文档。</summary>
    public const string Restore = "document.host_documents.restore";
}

/// <summary>
/// 文档回收站子资源稳定权限码。
/// </summary>
public static class HostDocumentRecycleBinPermissions
{
    /// <summary>允许读取回收站中的软删除文档列表。</summary>
    public const string Read = "document.host_recycle_bin.read";

    /// <summary>允许将回收站中的单个或批量文档恢复回主列表。</summary>
    public const string Restore = "document.host_recycle_bin.restore";

    /// <summary>允许永久清除回收站中已超过保留期的文档，不可恢复。</summary>
    public const string Purge = "document.host_recycle_bin.purge";
}

/// <summary>
/// 文档精确权限管理子资源稳定权限码。
/// </summary>
public static class HostDocumentPermissionManagementPermissions
{
    /// <summary>允许读取某文档已分配的用户权限列表。</summary>
    public const string Read = "document.host_permissions.read";

    /// <summary>允许整体覆盖设置文档的精确用户权限集合。</summary>
    public const string Set = "document.host_permissions.set";
}

/// <summary>
/// 文档匿名分享子资源稳定权限码。
/// </summary>
public static class HostDocumentSharePermissions
{
    /// <summary>允许读取文档分享列表与分享详情（不含口令明文）。</summary>
    public const string Read = "document.host_shares.read";

    /// <summary>允许为文档创建新的匿名分享链接。</summary>
    public const string Create = "document.host_shares.create";

    /// <summary>允许启用或停用文档分享链接的访问入口。</summary>
    public const string UpdateStatus = "document.host_shares.update_status";
}

/// <summary>
/// 文档统计子资源稳定权限码。
/// </summary>
public static class HostDocumentStatisticsPermissions
{
    /// <summary>允许读取文档总量、分类分组与今日活动统计看板数据。</summary>
    public const string Read = "document.host_statistics.read";
}
