using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// Host 文档细粒度权限的 Dapper SQL 语句集。所有语句均使用 SqlDataScope.HostOnly，
/// SQL 自身以 TenantId IS NULL 限制 Host 行；权限记录无 Version 字段，
/// 通过 DeleteByDocument + 批量 Insert 实现整文档权限重写的原子语义，禁止在内存层做 diff 后部分更新。
/// </summary>
internal static class DocumentPermissionSql
{
    /// <summary>权限记录投影字段；不含审计字段，仅用于权限目录列表。</summary>
    private const string Projection = """
        Id, DocumentId, UserId, PermissionLevel, CreatedAtUtc
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
            (Id, TenantId, DocumentId, UserId, PermissionLevel, CreatedAtUtc)
        VALUES
            (@Id, NULL, @DocumentId, @UserId, @PermissionLevel, @CreatedAtUtc)
        """,
        SqlDataScope.HostOnly);
}
