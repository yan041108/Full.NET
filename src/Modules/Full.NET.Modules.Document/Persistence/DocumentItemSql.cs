using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

internal static class DocumentItemSql
{
    private const string ItemProjection = """
        i.Id, i.Title, i.Description, i.CategoryId, i.CurrentVersionId,
        i.CreatedAtUtc, i.CreatedByUserId, i.UpdatedAtUtc, i.UpdatedByUserId, i.Version
        """;

    private const string DetailProjection = """
        i.Id, i.Title, i.Description, i.CategoryId, i.CurrentVersionId,
        i.CreatedAtUtc, i.CreatedByUserId, i.UpdatedAtUtc, i.UpdatedByUserId, i.Version,
        v.Id AS VersionId, v.VersionNumber, v.FileId, v.ContentHash, v.SizeBytes,
        v.CreatedAtUtc AS VersionCreatedAtUtc, v.UploadedByUserId
        """;

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

    public static readonly SqlStatement FindActiveById = new(
        "document.host_item.find_active_by_id",
        $$"""
        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.Id = @Id AND i.TenantId IS NULL AND i.IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindAnyById = new(
        "document.host_item.find_any_by_id",
        $$"""
        SELECT {{DetailProjection}}
        FROM fn_document_item AS i
        LEFT JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
        WHERE i.Id = @Id AND i.TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

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

    public static readonly SqlStatement Restore = new(
        "document.host_item.restore",
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

    public static readonly SqlStatement NextVersionNumber = new(
        "document.host_item.next_version_number",
        """
        SELECT COALESCE(MAX(VersionNumber), 0) + 1
        FROM fn_document_version
        WHERE DocumentItemId = @DocumentItemId
        """,
        SqlDataScope.HostOnly);

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
}
