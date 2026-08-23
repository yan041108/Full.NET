using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// Host 文档项与版本表的 Dapper SQL 语句集。所有语句均使用 SqlDataScope.HostOnly，
/// SQL 自身以 TenantId IS NULL 限制 Host 行；分页语句成对提供 SQL Server 与 MySQL 实现，
/// 投影列顺序与 DocumentItemDetailRecord 属性顺序对齐以支持 Dapper 直接映射。
/// </summary>
internal static class DocumentItemSql
{
    /// <summary>文档项列表投影字段；不含版本关联列，仅用于轻量列表场景。</summary>
    private const string ItemProjection = """
        i.Id, i.Title, i.Description, i.CategoryId, i.CurrentVersionId,
        i.CreatedAtUtc, i.CreatedByUserId, i.UpdatedAtUtc, i.UpdatedByUserId, i.Version,
        i.DeletedAtUtc, i.DeletedByUserId
        """;

    /// <summary>
    /// 文档明细投影：基表字段 + 当前版本 LEFT JOIN 字段；与 DocumentItemDetailRecord 一一对应，
    /// 缺失版本时版本字段返回 NULL，由 Mapper 防御性转换。
    /// </summary>
    private const string DetailProjection = """
        i.Id, i.Title, i.Description, i.CategoryId, i.CurrentVersionId,
        i.CreatedAtUtc, i.CreatedByUserId, i.UpdatedAtUtc, i.UpdatedByUserId, i.Version,
        v.Id AS VersionId, v.VersionNumber, v.FileId, v.ContentHash, v.SizeBytes,
        v.CreatedAtUtc AS VersionCreatedAtUtc, v.UploadedByUserId,
        v.FileName, v.MimeType, v.Extension, v.SizeBytes AS FileSizeBytes,
        i.DeletedAtUtc, i.DeletedByUserId
        """;

    /// <summary>
    /// 活动文档项分页（SQL Server）：先 COUNT 再分页详情，按 UpdatedAtUtc/CreatedAtUtc 取最近变更优先；
    /// OFFSET/FETCH NEXT 语法仅在 SQL Server 中支持，过滤条件显式 TenantId IS NULL AND IsDeleted = 0。
    /// </summary>
    public static readonly SqlStatement PageSqlServer = new(
        "document.host_item.page.sql_server",
        $$"""
        SELECT COUNT(1)
        FROM fn_document_item
        WHERE TenantId IS NULL AND IsDeleted = 0;

        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        ORDER BY COALESCE(i.UpdatedAtUtc, i.CreatedAtUtc) DESC, i.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 活动文档项分页（MySQL）：与 SQL Server 版本语义等价，使用 LIMIT/OFFSET 语法；
    /// 排序键相同以保证跨库分页结果一致。
    /// </summary>
    public static readonly SqlStatement PageMySql = new(
        "document.host_item.page.my_sql",
        $$"""
        SELECT COUNT(1)
        FROM fn_document_item
        WHERE TenantId IS NULL AND IsDeleted = 0;

        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        ORDER BY COALESCE(i.UpdatedAtUtc, i.CreatedAtUtc) DESC, i.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 回收站分页（SQL Server）：与活动列表相比仅切换 IsDeleted = 1，按删除时间倒序。
    /// </summary>
    public static readonly SqlStatement RecyclePageSqlServer = new(
        "document.host_recycle_bin.page.sql_server",
        $$"""
        SELECT COUNT(1)
        FROM fn_document_item
        WHERE TenantId IS NULL AND IsDeleted = 1;

        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 1
        ORDER BY i.DeletedAtUtc DESC, i.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    /// <summary>回收站分页（MySQL）：与 SQL Server 版本语义等价。</summary>
    public static readonly SqlStatement RecyclePageMySql = new(
        "document.host_recycle_bin.page.my_sql",
        $$"""
        SELECT COUNT(1)
        FROM fn_document_item
        WHERE TenantId IS NULL AND IsDeleted = 1;

        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 1
        ORDER BY i.DeletedAtUtc DESC, i.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    /// <summary>按 Id 查找未删除的文档项明细；找不到返回 NULL。</summary>
    public static readonly SqlStatement FindActiveById = new(
        "document.host_item.find_active_by_id",
        $$"""
        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.Id = @Id AND i.TenantId IS NULL AND i.IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按 Id 查找任意状态的文档项明细（含已删除）；回收站恢复流程使用，
    /// 业务查询禁止使用以避免泄漏软删除数据。
    /// </summary>
    public static readonly SqlStatement FindAnyById = new(
        "document.host_item.find_any_by_id",
        $$"""
        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.Id = @Id AND i.TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    /// <summary>按 Id 查找已删除文档项明细；仅回收站读取使用。</summary>
    public static readonly SqlStatement FindDeletedById = new(
        "document.host_recycle_bin.find_deleted_by_id",
        $$"""
        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.Id = @Id AND i.TenantId IS NULL AND i.IsDeleted = 1
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 新建文档项；CategoryId 与 CurrentVersionId 创建时为 NULL，由后续 Update 或 SetCurrentVersion 维护。
    /// TenantId 固定 NULL 以保证 Host 行；Version 初始为 1。
    /// </summary>
    public static readonly SqlStatement Insert = new(
        "document.host_item.insert",
        """
        INSERT INTO fn_document_item
            (Id, TenantId, CategoryId, CurrentVersionId, Title, Description,
             IsDeleted, DeletedAtUtc, DeletedByUserId,
             CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, Version)
        VALUES
            (@Id, NULL, NULL, NULL, @Title, @Description,
             0, NULL, NULL,
             @CreatedAtUtc, @CreatedByUserId, NULL, NULL, @Version)
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 更新文档项 Title/Description 等可变字段；WHERE 子句包含 Version 乐观并发校验，
    /// 仅在 IsDeleted = 0 时更新；受影响行数为 0 表示版本冲突或文档已被删除。
    /// </summary>
    public static readonly SqlStatement Update = new(
        "document.host_item.update",
        """
        UPDATE fn_document_item
        SET Title = @Title,
            Description = @Description,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 切换文档项的当前版本指针；必须与 Version 乐观校验一起执行，
    /// 上层调用方负责先通过 HostFileReferenceClaimService 确认版本引用归属。
    /// </summary>
    public static readonly SqlStatement SetCurrentVersion = new(
        "document.host_item.set_current_version",
        """
        UPDATE fn_document_item
        SET CurrentVersionId = @CurrentVersionId,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 软删除文档项：仅置位 IsDeleted 与删除审计字段，物理行保留以便回收站恢复；
    /// 受影响行数为 0 表示版本冲突或文档已删除，必须返回 409 Conflict。
    /// </summary>
    public static readonly SqlStatement SoftDelete = new(
        "document.host_item.soft_delete",
        """
        UPDATE fn_document_item
        SET IsDeleted = 1,
            DeletedAtUtc = @DeletedAtUtc,
            DeletedByUserId = @DeletedByUserId,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 从回收站恢复文档项：清空 IsDeleted 与删除审计字段，并写入恢复操作者；
    /// 仅在 IsDeleted = 1 时执行，受影响行数为 0 表示版本冲突。
    /// </summary>
    public static readonly SqlStatement Restore = new(
        "document.host_recycle_bin.restore",
        """
        UPDATE fn_document_item
        SET IsDeleted = 0,
            DeletedAtUtc = NULL,
            DeletedByUserId = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 1
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 物理删除回收站文档项：仅对 IsDeleted = 1 的行执行，不可逆；
    /// 上层调用方必须先解除文件引用 Claim，否则会留下孤儿文件引用。
    /// </summary>
    public static readonly SqlStatement Purge = new(
        "document.host_recycle_bin.purge",
        """
        DELETE FROM fn_document_item
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 1
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 取文档项下一版本号：COALESCE(MAX(VersionNumber), 0) + 1；
    /// 调用方必须在同事务内先持有规则行级锁，避免并发产生重复版本号。
    /// </summary>
    public static readonly SqlStatement NextVersionNumber = new(
        "document.host_item.next_version_number",
        """
        SELECT COALESCE(MAX(VersionNumber), 0) + 1
        FROM fn_document_version
        WHERE DocumentItemId = @DocumentItemId
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 新建文档版本记录；VersionNumber 由调用方基于 NextVersionNumber 计算后传入，
    /// FileId 必须已通过 HostFileReferenceClaimService 完成 Claim。
    /// </summary>
    public static readonly SqlStatement InsertVersion = new(
        "document.host_version.insert",
        """
        INSERT INTO fn_document_version
            (Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes,
             UploadedByUserId, CreatedAtUtc)
        VALUES
            (@Id, @DocumentItemId, @FileId, @VersionNumber, @ContentHash, @SizeBytes,
             @UploadedByUserId, @CreatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 判断指定 FileId 是否被任意版本引用；用于 Files 模块的保留期回收决策。
    /// 任意引用存在即返回 1，否则返回 0。
    /// </summary>
    public static readonly SqlStatement IsFileReferenced = new(
        "document.host_version.is_file_referenced",
        """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM fn_document_version
            WHERE FileId = @FileId
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按 VersionId 与 FileId 双键校验版本引用归属；
    /// Files 模块在 Confirm Release 前必须验证引用确实属于该版本。
    /// </summary>
    public static readonly SqlStatement VersionExistsByIdAndFile = new(
        "document.host_version.exists_by_id_and_file",
        """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM fn_document_version
            WHERE Id = @VersionId
              AND FileId = @FileId
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 统计概览（SQL Server）：返回文档总数、版本总数与总大小(KB)；
    /// 使用 ISNULL 处理 SUM NULL，过滤条件统一为 TenantId IS NULL AND IsDeleted = 0。
    /// </summary>
    public static readonly SqlStatement StatisticsSummarySqlServer = new(
        "document.host_statistics.summary.sql_server",
        """
        SELECT
            (SELECT COUNT(1) FROM fn_document_item WHERE TenantId IS NULL AND IsDeleted = 0) AS TotalItems,
            (SELECT COUNT(1) FROM fn_document_version v
             INNER JOIN fn_document_item i ON i.Id = v.DocumentItemId
             WHERE i.TenantId IS NULL AND i.IsDeleted = 0) AS TotalVersions,
            ISNULL((SELECT SUM(v.SizeBytes) / 1024 FROM fn_document_version v
             INNER JOIN fn_document_item i ON i.Id = v.DocumentItemId
             WHERE i.TenantId IS NULL AND i.IsDeleted = 0), 0) AS TotalSizeKb;
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 统计概览（MySQL）：与 SQL Server 版本语义等价，使用 IFNULL 替代 ISNULL；
    /// 两库函数语义一致，但语法不同因此必须成对维护。
    /// </summary>
    public static readonly SqlStatement StatisticsSummaryMySql = new(
        "document.host_statistics.summary.my_sql",
        """
        SELECT
            (SELECT COUNT(1) FROM fn_document_item WHERE TenantId IS NULL AND IsDeleted = 0) AS TotalItems,
            (SELECT COUNT(1) FROM fn_document_version v
             INNER JOIN fn_document_item i ON i.Id = v.DocumentItemId
             WHERE i.TenantId IS NULL AND i.IsDeleted = 0) AS TotalVersions,
            IFNULL((SELECT SUM(v.SizeBytes) / 1024 FROM fn_document_version v
             INNER JOIN fn_document_item i ON i.Id = v.DocumentItemId
             WHERE i.TenantId IS NULL AND i.IsDeleted = 0), 0) AS TotalSizeKb;
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按文件扩展名统计（SQL Server）：聚合版本大小，使用 ISNULL 处理空 SUM；
    /// 用于按类型展示的存储分布视图。
    /// </summary>
    public static readonly SqlStatement StatisticsByTypeSqlServer = new(
        "document.host_statistics.by_type.sql_server",
        """
        SELECT
            v.Extension,
            COUNT(1) AS Count,
            ISNULL(SUM(v.SizeBytes) / 1024, 0) AS TotalSizeKb
        FROM fn_document_version v
        INNER JOIN fn_document_item i ON i.Id = v.DocumentItemId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        GROUP BY v.Extension
        ORDER BY Count DESC;
        """,
        SqlDataScope.HostOnly);

    /// <summary>按文件扩展名统计（MySQL）：与 SQL Server 版本语义等价，使用 IFNULL。</summary>
    public static readonly SqlStatement StatisticsByTypeMySql = new(
        "document.host_statistics.by_type.my_sql",
        """
        SELECT
            v.Extension,
            COUNT(1) AS Count,
            IFNULL(SUM(v.SizeBytes) / 1024, 0) AS TotalSizeKb
        FROM fn_document_version v
        INNER JOIN fn_document_item i ON i.Id = v.DocumentItemId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        GROUP BY v.Extension
        ORDER BY Count DESC;
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按分类统计文档数量；LEFT JOIN 容忍未分类文档，按 Count DESC 排序。
    /// 此查询无 Provider 分歧，单语句兼容 SQL Server 与 MySQL。
    /// </summary>
    public static readonly SqlStatement StatisticsByCategory = new(
        "document.host_statistics.by_category",
        """
        SELECT
            i.CategoryId,
            c.Name AS CategoryName,
            COUNT(1) AS Count
        FROM fn_document_item i
        LEFT JOIN fn_document_category c ON c.Id = i.CategoryId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        GROUP BY i.CategoryId, c.Name
        ORDER BY Count DESC;
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 列出文档项的全部版本；按 VersionNumber DESC 倒序，最近版本在前。
    /// </summary>
    public static readonly SqlStatement ListVersionsByItemId = new(
        "document.host_item.versions.list",
        """
        SELECT
            Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes,
            ChangeDescription, UploadedByUserId, CreatedAtUtc
        FROM fn_document_version
        WHERE DocumentItemId = @DocumentItemId
        ORDER BY VersionNumber DESC
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按版本 Id 与文档项 Id 双键查版本；文档项 Id 必须匹配以防止跨文档取版本。
    /// </summary>
    public static readonly SqlStatement FindVersionById = new(
        "document.host_item.versions.find_by_id",
        """
        SELECT
            Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes,
            ChangeDescription, UploadedByUserId, CreatedAtUtc
        FROM fn_document_version
        WHERE Id = @VersionId
          AND DocumentItemId = @DocumentItemId
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 分享与访问统计（SQL Server）：聚合分享总数、今日访问/下载/创建/回收站数量；
    /// 使用 GETUTCDATE() 与 CAST(... AS DATE) 做当日对齐，确保 Host 行边界统一。
    /// </summary>
    public static readonly SqlStatement StatisticsShareCountSqlServer = new(
        "document.host_statistics.share_count.sql_server",
        """
        SELECT
            (SELECT COUNT(1) FROM fn_document_share WHERE TenantId IS NULL) AS ShareCount,
            (SELECT ISNULL(SUM(s.AccessCount), 0) FROM fn_document_share s
             WHERE s.TenantId IS NULL AND CAST(s.CreatedAtUtc AS DATE) = CAST(GETUTCDATE() AS DATE)) AS TodayAccessCount,
            (SELECT COUNT(1) FROM fn_document_item i
             WHERE i.TenantId IS NULL AND i.IsDeleted = 0
               AND i.LastAccessTime IS NOT NULL
               AND CAST(i.LastAccessTime AS DATE) = CAST(GETUTCDATE() AS DATE)) AS TodayDownloadCount,
            (SELECT COUNT(1) FROM fn_document_share s
             WHERE s.TenantId IS NULL AND CAST(s.CreatedAtUtc AS DATE) = CAST(GETUTCDATE() AS DATE)) AS TodayCreatedCount,
            (SELECT COUNT(1) FROM fn_document_item WHERE TenantId IS NULL AND IsDeleted = 1) AS RecycleBinCount;
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 分享与访问统计（MySQL）：与 SQL Server 版本语义等价，使用 UTC_DATE() 与 DATE() 做当日对齐；
    /// 日期函数差异是两库分叉的关键点，禁止合并为单语句。
    /// </summary>
    public static readonly SqlStatement StatisticsShareCountMySql = new(
        "document.host_statistics.share_count.my_sql",
        """
        SELECT
            (SELECT COUNT(1) FROM fn_document_share WHERE TenantId IS NULL) AS ShareCount,
            (SELECT IFNULL(SUM(s.AccessCount), 0) FROM fn_document_share s
             WHERE s.TenantId IS NULL AND DATE(s.CreatedAtUtc) = UTC_DATE()) AS TodayAccessCount,
            (SELECT COUNT(1) FROM fn_document_item i
             WHERE i.TenantId IS NULL AND i.IsDeleted = 0
               AND i.LastAccessTime IS NOT NULL
               AND DATE(i.LastAccessTime) = UTC_DATE()) AS TodayDownloadCount,
            (SELECT COUNT(1) FROM fn_document_share s
             WHERE s.TenantId IS NULL AND DATE(s.CreatedAtUtc) = UTC_DATE()) AS TodayCreatedCount,
            (SELECT COUNT(1) FROM fn_document_item WHERE TenantId IS NULL AND IsDeleted = 1) AS RecycleBinCount;
        """,
        SqlDataScope.HostOnly);
}
