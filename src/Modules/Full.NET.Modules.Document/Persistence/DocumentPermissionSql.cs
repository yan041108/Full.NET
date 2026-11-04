using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// Host 文档细粒度权限的 Dapper SQL 语句集。所有语句均使用 SqlDataScope.HostOnly，
/// SQL 自身以 TenantId IS NULL 限制 Host 行；权限记录无 Version 字段，
/// 通过 DeleteByDocument + 批量 Insert 实现整文档权限重写的原子语义，禁止在内存层做 diff 后部分更新。
/// </summary>
internal static class DocumentPermissionSql
{
    /// <summary>关联资源用完整文档子查询复用 ACL，不能只替换 Id 而遗漏状态和可见模式别名。</summary>
    internal static string RelatedReadPredicate(string documentIdColumn) =>
        $"EXISTS (SELECT 1 FROM fn_document_item i WHERE i.Id = {documentIdColumn} AND i.TenantId IS NULL AND {ReadPredicate})";
    /// <summary>无文档授权时按模块权限访问；有授权后仅允许明确授权用户。</summary>
    internal const string ReadPredicate = """
        (@ActorUserId IS NOT NULL
         AND (i.VisibilityMode = 'host' OR EXISTS (SELECT 1 FROM fn_document_permission acl
              WHERE acl.TenantId IS NULL AND acl.DocumentId = i.Id AND acl.UserId = @ActorUserId AND acl.Actions > 0))
         AND (i.Status <> 0 OR i.CreatedByUserId = @ActorUserId OR EXISTS (SELECT 1 FROM fn_document_permission acl
              WHERE acl.TenantId IS NULL AND acl.DocumentId = i.Id AND acl.UserId = @ActorUserId AND acl.Actions > 0)))
        """;
    public static readonly SqlStatement CheckAccess = new("document.host_permission.check_access", """
        SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM fn_document_item WHERE Id = @DocumentId AND TenantId IS NULL) THEN 1
        WHEN EXISTS (SELECT 1 FROM fn_document_item i WHERE i.Id = @DocumentId AND i.TenantId IS NULL
          AND @ActorUserId IS NOT NULL
          AND (i.VisibilityMode = 'host' OR EXISTS (SELECT 1 FROM fn_document_permission acl WHERE acl.DocumentId = i.Id
               AND acl.TenantId IS NULL AND acl.UserId = @ActorUserId AND (acl.Actions & @OperationMask) <> 0))
          AND (i.Status <> 0 OR i.CreatedByUserId = @ActorUserId OR EXISTS (SELECT 1 FROM fn_document_permission acl
               WHERE acl.DocumentId = i.Id AND acl.TenantId IS NULL AND acl.UserId = @ActorUserId AND (acl.Actions & @OperationMask) <> 0)))
        THEN 1 ELSE 0 END
        """, SqlDataScope.HostOnly);

    // 先锁定拥有修订号的父行，再读取授权。写入同样先更新父行，避免空集合和双查询的混合快照。
    public static readonly SqlStatement LockDocumentSqlServer = new(
        "document.host_permission.lock_document.sql_server",
        """
        SELECT Id, Version, VisibilityMode
        FROM fn_document_item WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement LockDocumentMySql = new(
        "document.host_permission.lock_document.my_sql",
        """
        SELECT Id, Version, VisibilityMode
        FROM fn_document_item
        WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
        FOR UPDATE
        """,
        SqlDataScope.HostOnly);

    /// <summary>权限记录投影字段；不含审计字段，仅用于权限目录列表。</summary>
    private const string Projection = """
        Id, DocumentId, UserId, Actions, CreatedAtUtc
        """;

    /// <summary>
    /// 列出指定文档的全部权限记录，按创建时间倒序；用于权限管理页展示当前文档的授权矩阵。
    /// </summary>
    public static readonly SqlStatement ListByDocument = new(
        "document.host_permission.list_by_document",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_permission
        WHERE TenantId IS NULL AND DocumentId = @DocumentId
        ORDER BY CreatedAtUtc DESC, Id
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按文档物理删除全部权限记录；用于整文档权限重写的"先全删再批量插"流程，
    /// 必须与 Insert 在同一事务内执行，否则会引入权限真空窗口。
    /// </summary>
    public static readonly SqlStatement DeleteByDocument = new(
        "document.host_permission.delete_by_document",
        """
        DELETE FROM fn_document_permission
        WHERE TenantId IS NULL AND DocumentId = @DocumentId
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 插入单条权限记录；TenantId 显式置 NULL 标记 Host 行，
    /// 调用方必须保证 @Id 为外部已生成的 UUID v7，禁止依赖数据库自增。
    /// </summary>
    public static readonly SqlStatement Insert = new(
        "document.host_permission.insert",
        """
        INSERT INTO fn_document_permission
            (Id, TenantId, DocumentId, UserId, Actions, CreatedAtUtc)
        VALUES
            (@Id, NULL, @DocumentId, @UserId, @Actions, @CreatedAtUtc)
        """,
        SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertBatchSqlServer = new(
        "document.host_permission.insert_batch.sqlserver",
        """
        INSERT INTO fn_document_permission (Id, TenantId, DocumentId, UserId, Actions, CreatedAtUtc)
        SELECT rowData.Id, NULL, @DocumentId, rowData.UserId, rowData.Actions, @CreatedAtUtc FROM OPENJSON(@Rows) WITH (Id uniqueidentifier '$.id', UserId uniqueidentifier '$.userId', Actions int '$.actions') AS rowData
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertBatchMySql = new(
        "document.host_permission.insert_batch.mysql",
        """
        INSERT INTO fn_document_permission (Id, TenantId, DocumentId, UserId, Actions, CreatedAtUtc)
        SELECT UNHEX(REPLACE(rowData.Id, '-', '')), NULL, @DocumentId, UNHEX(REPLACE(rowData.UserId, '-', '')), rowData.Actions, @CreatedAtUtc FROM JSON_TABLE(@Rows, '$[*]' COLUMNS (Id CHAR(36) PATH '$.id', UserId CHAR(36) PATH '$.userId', Actions INT PATH '$.actions')) AS rowData
        """, SqlDataScope.HostOnly);
}
