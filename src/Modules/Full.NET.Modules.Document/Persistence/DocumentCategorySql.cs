using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

internal static class DocumentCategorySql
{
    private const string Projection = """
        Id, ParentId, Name, SortOrder, Code, Icon, Color, Description, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    public static readonly SqlStatement ListActive = new(
        "document.host_category.list_active",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_category
        WHERE TenantId IS NULL AND IsDeleted = 0
        ORDER BY SortOrder, Name, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveById = new(
        "document.host_category.find_active_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_category
        WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveByParentAndName = new(
        "document.host_category.find_active_by_parent_and_name",
        """
        SELECT Id, Name, Version
        FROM fn_document_category
        WHERE TenantId IS NULL
          AND IsDeleted = 0
          AND ((@ParentId IS NULL AND ParentId IS NULL) OR ParentId = @ParentId)
          AND Name = @Name
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "document.host_category.insert",
        """
        INSERT INTO fn_document_category
            (Id, TenantId, ParentId, Name, SortOrder, Code, Icon, Color, Description,
             IsDeleted, DeletedAtUtc, DeletedByUserId,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, NULL, @ParentId, @Name, @SortOrder, @Code, @Icon, @Color, @Description,
             0, NULL, NULL,
             @CreatedAtUtc, NULL, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "document.host_category.update",
        """
        UPDATE fn_document_category
        SET ParentId = @ParentId,
            Name = @Name,
            SortOrder = @SortOrder,
            Code = @Code,
            Icon = @Icon,
            Color = @Color,
            Description = @Description,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsDeleted = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SoftDelete = new(
        "document.host_category.soft_delete",
        """
        UPDATE fn_document_category
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

    public static readonly SqlStatement CountActiveChildren = new(
        "document.host_category.count_active_children",
        """
        SELECT COUNT(1)
        FROM fn_document_category
        WHERE TenantId IS NULL
          AND IsDeleted = 0
          AND ParentId = @ParentId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveItems = new(
        "document.host_category.count_active_items",
        """
        SELECT COUNT(1)
        FROM fn_document_item
        WHERE TenantId IS NULL
          AND IsDeleted = 0
          AND CategoryId = @CategoryId
        """,
        SqlDataScope.HostOnly);
}
