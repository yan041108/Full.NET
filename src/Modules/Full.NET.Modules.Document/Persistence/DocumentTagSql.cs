using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

internal static class DocumentTagSql
{
    private const string Projection = """
        Id, Name, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    public static readonly SqlStatement ListActive = new(
        "document.host_tag.list_active",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_tag
        WHERE TenantId IS NULL AND IsDeleted = 0
        ORDER BY Name, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveById = new(
        "document.host_tag.find_active_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_tag
        WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveByName = new(
        "document.host_tag.find_active_by_name",
        """
        SELECT Id, Name, Version
        FROM fn_document_tag
        WHERE TenantId IS NULL AND IsDeleted = 0 AND Name = @Name
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "document.host_tag.insert",
        """
        INSERT INTO fn_document_tag
            (Id, TenantId, Name,
             IsDeleted, DeletedAtUtc, DeletedByUserId,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, NULL, @Name,
             0, NULL, NULL,
             @CreatedAtUtc, NULL, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "document.host_tag.update",
        """
        UPDATE fn_document_tag
        SET Name = @Name,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SoftDelete = new(
        "document.host_tag.soft_delete",
        """
        UPDATE fn_document_tag
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

    public static readonly SqlStatement CountAssignments = new(
        "document.host_tag.count_assignments",
        """
        SELECT COUNT(1)
        FROM fn_document_tag_assignment
        WHERE TagId = @TagId
        """,
        SqlDataScope.HostOnly);
}
