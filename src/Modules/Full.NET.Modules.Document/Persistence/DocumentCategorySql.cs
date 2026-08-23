using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// Host 文档分类的 Dapper SQL 语句集。所有语句均使用 SqlDataScope.HostOnly，
/// SQL 自身以 TenantId IS NULL 限制 Host 行；分类支持父子层级与软删除，删除前必须校验子分类与文档引用。
/// </summary>
internal static class DocumentCategorySql
{
    /// <summary>分类投影字段；包含 Code/Icon/Color/Description 展示列，与 DocumentCategoryRecord 顺序对齐。</summary>
    private const string Projection = """
        Id, ParentId, Name, SortOrder, Code, Icon, Color, Description, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    /// <summary>列出全部活动分类；按 SortOrder、Name、Id 排序以稳定层级展示。</summary>
    public static readonly SqlStatement ListActive = new(
        "document.host_category.list_active",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_category
        WHERE TenantId IS NULL AND IsDeleted = 0
        ORDER BY SortOrder, Name, Id
        """,
        SqlDataScope.HostOnly);

    /// <summary>按 Id 查找未删除的分类；找不到返回 NULL。</summary>
    public static readonly SqlStatement FindActiveById = new(
        "document.host_category.find_active_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_category
        WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 在同一父分类下按 Name 查找冲突；用于创建/改名时的同名校验。
    /// 父子层级通过 ParentId IS NULL OR ParentId = @ParentId 表达，避免 NULL 比较歧义。
    /// </summary>
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

    /// <summary>
    /// 新建分类；同时写入 Code/Icon/Color/Description 展示字段；
    /// TenantId 固定 NULL，IsDeleted 默认 0，Version 初始为 1。
    /// </summary>
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

    /// <summary>
    /// 更新分类的全部可变字段（含 Code/Icon/Color/Description）；WHERE 含 Version 乐观并发校验，
    /// 受影响行数为 0 表示版本冲突或分类已删除。
    /// </summary>
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

    /// <summary>
    /// 软删除分类；删除前必须先校验无活动子分类与无文档引用，避免破坏层级与引用完整性。
    /// </summary>
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

    /// <summary>统计指定父分类下的活动子分类数量；用于删除前层级完整性校验。</summary>
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

    /// <summary>统计指定分类下未删除的文档项数量；用于删除前引用完整性校验，存在引用必须返回 InUse 错误。</summary>
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
