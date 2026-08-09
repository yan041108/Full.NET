using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// 文档标签 Dapper SQL 语句集。投影列顺序与 DocumentTagRecord 保持一致，
/// 包含新增的 Code/Icon/Description 三列并与 Category 统一字段顺序。
/// </summary>
internal static class DocumentTagSql
{
    /// <summary>
    /// 标签投影列，顺序：Id, Name, Code, Icon, Color, Description, UseCount, CreatedAtUtc, UpdatedAtUtc, Version。
    /// 与 DocumentTagRecord 属性顺序对齐，确保 Dapper 直接映射。
    /// </summary>
    private const string Projection = """
        Id, Name, Code, Icon, Color, Description, UseCount, CreatedAtUtc, UpdatedAtUtc, Version
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

    /// <summary>
    /// 插入标签。新增 Code/Icon/Color/Description 列，列顺序与 Projection 中的业务字段顺序一致。
    /// </summary>
    public static readonly SqlStatement Insert = new(
        "document.host_tag.insert",
        """
        INSERT INTO fn_document_tag
            (Id, TenantId, Name, Code, Icon, Color, Description, UseCount,
             IsDeleted, DeletedAtUtc, DeletedByUserId,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, NULL, @Name, @Code, @Icon, @Color, @Description, @UseCount,
             0, NULL, NULL,
             @CreatedAtUtc, NULL, @Version)
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 更新标签。同步写入 Code/Icon/Color/Description 四列，保持与 Category 更新语句的字段顺序一致。
    /// </summary>
    public static readonly SqlStatement Update = new(
        "document.host_tag.update",
        """
        UPDATE fn_document_tag
        SET Name = @Name,
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
