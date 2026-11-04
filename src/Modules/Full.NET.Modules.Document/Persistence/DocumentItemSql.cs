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
        i.DocumentNo, i.DocumentType, i.Status, i.Sort, i.Thumbnail, i.AccessCount, i.LastAccessTime,
        i.VisibilityMode, i.AccessRevision, i.LastVersionNumber, i.IsPurging,
        (SELECT c.Name FROM fn_document_category c WHERE c.Id = i.CategoryId) AS CategoryName,
        (SELECT c.Color FROM fn_document_category c WHERE c.Id = i.CategoryId) AS CategoryColor,
        i.CreatedAtUtc, i.CreatedByUserId, i.UpdatedAtUtc, i.UpdatedByUserId, i.Version,
        i.DeletedAtUtc, i.DeletedByUserId
        """;

    /// <summary>
    /// 文档明细投影：基表字段 + 当前版本 LEFT JOIN 字段；与 DocumentItemDetailRecord 一一对应，
    /// 缺失版本时版本字段返回 NULL，由 Mapper 防御性转换。
    /// </summary>
    private const string DetailProjection = """
        i.Id, i.Title, i.Description, i.CategoryId, i.CurrentVersionId,
        i.DocumentNo, i.DocumentType, i.Status, i.Sort, i.Thumbnail, i.AccessCount, i.LastAccessTime,
        i.VisibilityMode, i.AccessRevision, i.LastVersionNumber, i.IsPurging,
        (SELECT c.Name FROM fn_document_category c WHERE c.Id = i.CategoryId) AS CategoryName,
        (SELECT c.Color FROM fn_document_category c WHERE c.Id = i.CategoryId) AS CategoryColor,
        i.CreatedAtUtc, i.CreatedByUserId, i.UpdatedAtUtc, i.UpdatedByUserId, i.Version,
        v.Id AS VersionId, v.VersionNumber, v.FileId, v.ContentHash, v.SizeBytes,
        v.CreatedAtUtc AS VersionCreatedAtUtc, v.UploadedByUserId,
        v.FileName, v.MimeType, v.Extension, v.ChangeDescription, v.SizeBytes AS FileSizeBytes,
        i.DeletedAtUtc, i.DeletedByUserId
        """;

    /// <summary>
    /// 活动文档项分页（SQL Server）：先 COUNT 再分页详情，按 UpdatedAtUtc/CreatedAtUtc 取最近变更优先；
    /// OFFSET/FETCH NEXT 语法仅在 SQL Server 中支持，过滤条件显式 TenantId IS NULL AND IsDeleted = 0。
    /// </summary>
    private const string PageFilter = """
          AND (@Keyword IS NULL OR i.Title LIKE @Keyword ESCAPE '~' OR i.DocumentNo LIKE @Keyword ESCAPE '~')
          AND (@Status IS NULL OR i.Status = @Status)
          AND (@DocumentType IS NULL OR i.DocumentType = @DocumentType)
          AND (@CategoryId IS NULL OR i.CategoryId IN (SELECT Id FROM category_tree))
          AND (@TagId IS NULL OR EXISTS (
              SELECT 1
              FROM fn_document_tag_assignment AS assignment
              WHERE assignment.DocumentItemId = i.Id
                AND assignment.TagId = @TagId
          ))
        """;

    // 深度上限与写入守卫一致；旧数据含环时仍能终止，IN 不重复文档。
    private const string CategoryTree = """
        category_tree AS (
            SELECT Id, 0 AS Depth FROM fn_document_category
            WHERE Id = @CategoryId AND TenantId IS NULL AND IsDeleted = 0
            UNION ALL
            SELECT c.Id, tree.Depth + 1 FROM fn_document_category c
            INNER JOIN category_tree tree ON c.ParentId = tree.Id
            WHERE c.TenantId IS NULL AND c.IsDeleted = 0 AND tree.Depth < 128
        )
        """;

    public static readonly SqlStatement PageSqlServer = new(
        "document.host_item.page.sql_server",
        $$"""
        WITH {{CategoryTree}}
        SELECT COUNT(1)
        FROM fn_document_item AS i
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        AND {{DocumentPermissionSql.ReadPredicate}}
        {{PageFilter}} OPTION (MAXRECURSION 128);

        WITH {{CategoryTree}}
        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        AND {{DocumentPermissionSql.ReadPredicate}}
        {{PageFilter}}
        ORDER BY COALESCE(i.UpdatedAtUtc, i.CreatedAtUtc) DESC, i.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY OPTION (MAXRECURSION 128)
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 活动文档项分页（MySQL）：与 SQL Server 版本语义等价，使用 LIMIT/OFFSET 语法；
    /// 排序键相同以保证跨库分页结果一致。
    /// </summary>
    public static readonly SqlStatement PageMySql = new(
        "document.host_item.page.my_sql",
        $$"""
        WITH RECURSIVE {{CategoryTree}}
        SELECT COUNT(1)
        FROM fn_document_item AS i
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        AND {{DocumentPermissionSql.ReadPredicate}}
        {{PageFilter}};

        WITH RECURSIVE {{CategoryTree}}
        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 0
        AND {{DocumentPermissionSql.ReadPredicate}}
        {{PageFilter}}
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
        FROM fn_document_item AS i
        WHERE i.TenantId IS NULL AND i.IsDeleted = 1
          AND {{DocumentPermissionSql.ReadPredicate}};

        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 1
          AND {{DocumentPermissionSql.ReadPredicate}}
        ORDER BY i.DeletedAtUtc DESC, i.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    /// <summary>回收站分页（MySQL）：与 SQL Server 版本语义等价。</summary>
    public static readonly SqlStatement RecyclePageMySql = new(
        "document.host_recycle_bin.page.my_sql",
        $$"""
        SELECT COUNT(1)
        FROM fn_document_item AS i
        WHERE i.TenantId IS NULL AND i.IsDeleted = 1
          AND {{DocumentPermissionSql.ReadPredicate}};

        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.TenantId IS NULL AND i.IsDeleted = 1
          AND {{DocumentPermissionSql.ReadPredicate}}
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

    /// <summary>未分类标题写入共享事务锁，直到文档写入提交或回滚才释放。</summary>
    public static readonly SqlStatement LockUnclassifiedWriteSqlServer = new(
        "document.host_item.lock_unclassified_write.sql_server",
        """
        SELECT 1 FROM fn_document_item_write_guard WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id AND TenantId IS NULL
        """, SqlDataScope.HostOnly);

    /// <summary>MySQL 与 SQL Server 使用同一门禁行，避免 ReadCommitted 下的空结果竞争。</summary>
    public static readonly SqlStatement LockUnclassifiedWriteMySql = new(
        "document.host_item.lock_unclassified_write.my_sql",
        """
        SELECT 1 FROM fn_document_item_write_guard
        WHERE Id = @Id AND TenantId IS NULL FOR UPDATE
        """, SqlDataScope.HostOnly);

    /// <summary>按最终分类判断标题冲突；标量计数允许安全处理既有多条同名记录。</summary>
    public static readonly SqlStatement CountActiveByTitle = new(
        "document.host_item.count_active_by_title",
        """
        SELECT COUNT(1)
        FROM fn_document_item
        WHERE TenantId IS NULL
          AND IsDeleted = 0
          AND Title = @Title
          AND (
              (CategoryId = @CategoryId)
              OR (CategoryId IS NULL AND @CategoryId IS NULL)
          )
          AND (@ExcludeId IS NULL OR Id <> @ExcludeId)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "document.host_item.insert",
        """
        INSERT INTO fn_document_item
            (Id, TenantId, CategoryId, CurrentVersionId, Title, Description, DocumentNo,
             DocumentType, Status, Sort, Thumbnail,
             IsDeleted, DeletedAtUtc, DeletedByUserId,
             CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, Version)
        VALUES
            (@Id, NULL, @CategoryId, NULL, @Title, @Description, @DocumentNo,
             @DocumentType, @Status, @Sort, @Thumbnail,
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
            CategoryId = @CategoryId,
            Thumbnail = @Thumbnail,
            Sort = @Sort,
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
            AccessRevision = AccessRevision + 1,
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
            AccessRevision = AccessRevision + 1,
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
            CategoryId = @CategoryId,
            Status = 0,
            AccessRevision = AccessRevision + 1,
            DeletedAtUtc = NULL,
            DeletedByUserId = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 1 AND IsPurging = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 在活动文档项上执行乐观并发 Touch：仅递增 Version 并写入更新审计字段；
    /// 删除历史版本等不改变 CurrentVersionId 的写操作使用，防止并发覆盖。
    /// </summary>
    public static readonly SqlStatement TouchActiveItem = new(
        "document.host_item.touch_active",
        """
        UPDATE fn_document_item
        SET UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 物理删除回收站文档项：仅对 IsDeleted = 1 的行执行，不可逆；
    /// 上层调用方须先在版本删除事务写入可靠释放事实，不能依赖提交后的同步回调。
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

    /// <summary>主行先于版本、标签关联等子行锁定；删除状态由持锁调用方重新检查。</summary>
    public static readonly SqlStatement LockHostItemSqlServer = new(
        "document.host_item.lock_host.sql_server",
        """
        SELECT 1 FROM fn_document_item WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    /// <summary>MySQL与SQL Server使用相同主行锁边界，不依赖默认隔离级别或子行存在。</summary>
    public static readonly SqlStatement LockHostItemMySql = new(
        "document.host_item.lock_host.mysql",
        """
        SELECT 1 FROM fn_document_item
        WHERE Id = @Id AND TenantId IS NULL FOR UPDATE
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 取文档项下一版本号：COALESCE(MAX(VersionNumber), 0) + 1；
    /// 调用方必须在同事务内先持有规则行级锁，避免并发产生重复版本号。
    /// </summary>
    public static readonly SqlStatement NextVersionNumber = new(
        "document.host_item.next_version_number",
        """
        SELECT LastVersionNumber + 1 FROM fn_document_item WHERE Id = @DocumentItemId AND TenantId IS NULL
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
             ChangeDescription, FileName, MimeType, Extension, UploadedByUserId, CreatedAtUtc)
        VALUES
            (@Id, @DocumentItemId, @FileId, @VersionNumber, @ContentHash, @SizeBytes,
             @ChangeDescription, @FileName, @MimeType, @Extension, @UploadedByUserId, @CreatedAtUtc)
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
    /// <summary>批量引用端口复用已有版本物化器；只读 Document 自有版本表，包含回收站保留版本。</summary>
    public static readonly SqlStatement FindVersionsForClaimProbe = new(
        "document.host_version.find_for_claim_probe",
        """
        SELECT Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes,
               ChangeDescription, UploadedByUserId, CreatedAtUtc
        FROM fn_document_version
        WHERE Id IN @VersionIds
        """, SqlDataScope.HostOnly);

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

    // 看板共享 Published 与当前 ACL 范围，无版本文档仍计入总量；聚合字节后由 C# 换算。
    private static readonly string StatisticsScope =
        "i.TenantId IS NULL AND i.IsDeleted = 0 AND i.Status = 1 AND " + DocumentPermissionSql.ReadPredicate;

    public static readonly SqlStatement StatisticsSummarySqlServer = new(
        "document.host_statistics.summary.sql_server",
        $"""
        SELECT
            (SELECT COUNT(1) FROM fn_document_item i WHERE {StatisticsScope}) AS TotalItems,
            (SELECT COUNT(1) FROM fn_document_version v INNER JOIN fn_document_item i ON i.Id = v.DocumentItemId WHERE {StatisticsScope}) AS TotalVersions,
            (SELECT COALESCE(SUM(v.SizeBytes), 0) FROM fn_document_item i INNER JOIN fn_document_version v ON v.Id = i.CurrentVersionId WHERE {StatisticsScope}) AS TotalSizeBytes,
            (SELECT COALESCE(SUM(v.SizeBytes), 0) FROM fn_document_item i INNER JOIN fn_document_version v ON v.DocumentItemId = i.Id AND v.Id <> i.CurrentVersionId WHERE {StatisticsScope}) AS HistorySizeBytes
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement StatisticsSummaryMySql = new(
        "document.host_statistics.summary.my_sql", StatisticsSummarySqlServer.Text, SqlDataScope.HostOnly);
    public static readonly SqlStatement StatisticsByTypeSqlServer = new(
        "document.host_statistics.by_type.sql_server",
        $"""
        SELECT v.Extension, COUNT(1) AS Count, COALESCE(SUM(v.SizeBytes), 0) AS TotalSizeBytes
        FROM fn_document_item i INNER JOIN fn_document_version v ON v.Id = i.CurrentVersionId
        WHERE {StatisticsScope} GROUP BY v.Extension ORDER BY Count DESC, v.Extension
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement StatisticsByTypeMySql = new(
        "document.host_statistics.by_type.my_sql", StatisticsByTypeSqlServer.Text, SqlDataScope.HostOnly);
    public static readonly SqlStatement StatisticsByCategory = new(
        "document.host_statistics.by_category",
        $"""
        SELECT c.Id AS CategoryId, c.Name AS CategoryName, COUNT(1) AS Count
        FROM fn_document_item i LEFT JOIN fn_document_category c ON c.Id = i.CategoryId AND c.TenantId IS NULL AND c.IsDeleted = 0
        WHERE {StatisticsScope} GROUP BY c.Id, c.Name ORDER BY Count DESC, c.Id
        """, SqlDataScope.HostOnly);

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

    // IClock 提供 UTC 半开日区间，重试和 Range 的去重在访问事实写入时完成。
    public static readonly SqlStatement StatisticsShareCountSqlServer = new(
        "document.host_statistics.share_count.sql_server",
        $"""
        SELECT
            (SELECT COUNT(1) FROM fn_document_share s INNER JOIN fn_document_item i ON i.Id = s.DocumentId
             WHERE {StatisticsScope} AND s.TenantId IS NULL AND s.IsEnabled = 1 AND s.ExpireTime > @Now
               AND (s.MaxAccessCount IS NULL OR s.AccessCount < s.MaxAccessCount)) AS ShareCount,
            (SELECT COUNT(1) FROM fn_document_access_log log INNER JOIN fn_document_item i ON i.Id = log.DocumentItemId
             WHERE {StatisticsScope} AND log.OccurredAtUtc >= @DayStart AND log.OccurredAtUtc < @DayEnd) AS TodayAccessCount,
            (SELECT COUNT(1) FROM fn_document_access_log log INNER JOIN fn_document_item i ON i.Id = log.DocumentItemId
             WHERE {StatisticsScope} AND log.AccessTypeKey = 'download' AND log.OccurredAtUtc >= @DayStart AND log.OccurredAtUtc < @DayEnd) AS TodayDownloadCount,
            (SELECT COUNT(1) FROM fn_document_access_log log INNER JOIN fn_document_item i ON i.Id = log.DocumentItemId
             WHERE {StatisticsScope} AND log.AccessTypeKey = 'preview' AND log.OccurredAtUtc >= @DayStart AND log.OccurredAtUtc < @DayEnd) AS TodayPreviewCount,
            (SELECT COUNT(1) FROM fn_document_access_log log INNER JOIN fn_document_item i ON i.Id = log.DocumentItemId
             WHERE {StatisticsScope} AND log.AccessTypeKey = 'share_access' AND log.OccurredAtUtc >= @DayStart AND log.OccurredAtUtc < @DayEnd) AS TodayShareAccessCount,
            (SELECT COUNT(1) FROM fn_document_item i WHERE {StatisticsScope}
             AND i.CreatedAtUtc >= @DayStart AND i.CreatedAtUtc < @DayEnd) AS TodayCreatedCount,
            (SELECT COUNT(1) FROM fn_document_item i WHERE i.TenantId IS NULL AND i.IsDeleted = 1 AND {DocumentPermissionSql.ReadPredicate}) AS RecycleBinCount
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement StatisticsShareCountMySql = new(
        "document.host_statistics.share_count.my_sql", StatisticsShareCountSqlServer.Text, SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkPurging = new("document.host_item.mark_purging",
        "UPDATE fn_document_item SET IsPurging = 1, AccessRevision = AccessRevision + 1 WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 1 AND IsPurging = 0", SqlDataScope.HostOnly);
    public static readonly SqlStatement PurgeVersionsSqlServer = new("document.host_item.purge_versions.sql_server", """
        SELECT TOP (100) Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes, ChangeDescription, UploadedByUserId, CreatedAtUtc
        FROM fn_document_version WHERE DocumentItemId = @DocumentItemId ORDER BY Id
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement PurgeVersionsMySql = new("document.host_item.purge_versions.my_sql", """
        SELECT Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes, ChangeDescription, UploadedByUserId, CreatedAtUtc
        FROM fn_document_version WHERE DocumentItemId = @DocumentItemId ORDER BY Id LIMIT 100
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement CountVersionsByItemId = new(
        "document.host_item.versions.count",
        "SELECT COUNT(1) FROM fn_document_version WHERE DocumentItemId = @DocumentItemId", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListVersionsPageSqlServer = new(
        "document.host_item.versions.page.sql_server",
        ListVersionsByItemId.Text + " OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListVersionsPageMySql = new(
        "document.host_item.versions.page.mysql",
        ListVersionsByItemId.Text + " LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);

    /// <summary>写用例必须先取得根行锁，避免版本、ACL、状态和保留策略竞争。</summary>
    public static readonly SqlStatement LockSqlServer = new("document.host_item.lock.sql_server",
        FindAnyById.Text.Replace("FROM fn_document_item AS i", "FROM fn_document_item AS i WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal), SqlDataScope.HostOnly);
    public static readonly SqlStatement LockMySql = new("document.host_item.lock.my_sql",
        FindAnyById.Text + " FOR UPDATE", SqlDataScope.HostOnly);
    public static readonly SqlStatement AllocateVersion = new("document.host_item.allocate_version",
        "UPDATE fn_document_item SET LastVersionNumber = LastVersionNumber + 1 WHERE Id = @Id AND TenantId IS NULL", SqlDataScope.HostOnly);
    public static readonly SqlStatement ChangeStatus = new("document.host_item.change_status",
        "UPDATE fn_document_item SET Status = @Status, AccessRevision = AccessRevision + 1, Version = Version + 1, UpdatedAtUtc = @Now, UpdatedByUserId = @ActorUserId WHERE Id = @Id AND TenantId IS NULL AND Version = @Version AND IsDeleted = 0", SqlDataScope.HostOnly);
    public static readonly SqlStatement DisableShares = new("document.host_item.disable_shares",
        "UPDATE fn_document_share SET IsEnabled = 0, PolicyRevision = PolicyRevision + 1, Version = Version + 1 WHERE DocumentId = @Id AND TenantId IS NULL", SqlDataScope.HostOnly);
    public static readonly SqlStatement SetVisibility = new("document.host_item.set_visibility",
        "UPDATE fn_document_item SET VisibilityMode = @VisibilityMode, AccessRevision = AccessRevision + 1, Version = Version + 1 WHERE Id = @Id AND TenantId IS NULL AND Version = @Version", SqlDataScope.HostOnly);
}
