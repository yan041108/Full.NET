using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

internal static class DocumentShareSql
{
    private const string Projection = """
        Id, DocumentId, ShareCode, CreatedAtUtc, ExpireTime,
        PasswordHash, MaxAccessCount, AccessCount, IsEnabled, Version
        """;

    public static readonly SqlStatement PageSqlServer = new(
        "document.host_share.page.sql_server",
        $$"""
        SELECT COUNT(1)
        FROM fn_document_share
        WHERE TenantId IS NULL;

        SELECT {{Projection}}
        FROM fn_document_share
        WHERE TenantId IS NULL
        ORDER BY CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageMySql = new(
        "document.host_share.page.my_sql",
        $$"""
        SELECT COUNT(1)
        FROM fn_document_share
        WHERE TenantId IS NULL;

        SELECT {{Projection}}
        FROM fn_document_share
        WHERE TenantId IS NULL
        ORDER BY CreatedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "document.host_share.find_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_share
        WHERE Id = @Id AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByCode = new(
        "document.host_share.find_by_code",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_share
        WHERE ShareCode = @ShareCode AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "document.host_share.insert",
        """
        INSERT INTO fn_document_share
            (Id, TenantId, DocumentId, ShareCode, CreatedAtUtc, ExpireTime,
             PasswordHash, MaxAccessCount, AccessCount, IsEnabled, Version)
        VALUES
            (@Id, NULL, @DocumentId, @ShareCode, @CreatedAtUtc, @ExpireTime,
             @PasswordHash, @MaxAccessCount, 0, 1, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateStatus = new(
        "document.host_share.update_status",
        """
        UPDATE fn_document_share
        SET IsEnabled = @IsEnabled,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement TryConsumeAccess = new(
        "document.host_share.try_consume_access",
        """
        UPDATE fn_document_share
        SET AccessCount = AccessCount + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsEnabled = 1
          AND ExpireTime >= @Now
          AND (MaxAccessCount IS NULL OR AccessCount < MaxAccessCount)
        """,
        SqlDataScope.HostOnly);
}
