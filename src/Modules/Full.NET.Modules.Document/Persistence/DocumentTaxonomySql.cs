using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>分类、标签共享的技术门禁 SQL，只能在调用方事务中锁定固定 Host 行。</summary>
internal static class DocumentTaxonomySql
{
    public static readonly SqlStatement LockWriteSqlServer = new(
        "document.host_taxonomy.lock_write.sql_server",
        """
        SELECT 1 FROM fn_document_taxonomy_write_guard WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id AND TenantId IS NULL
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement LockWriteMySql = new(
        "document.host_taxonomy.lock_write.my_sql",
        """
        SELECT 1 FROM fn_document_taxonomy_write_guard
        WHERE Id = @Id AND TenantId IS NULL
        FOR UPDATE
        """, SqlDataScope.HostOnly);
}
