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
    /// 从真实Host引用派生UseCount，保留回收站引用；不读取可能漂移的历史计数列。
    /// </summary>
    private const string Projection = """
        tag.Id, tag.Name, tag.Code, tag.Icon, tag.Color, tag.Description,
        COALESCE(reference_counts.UseCount, 0) AS UseCount,
        tag.CreatedAtUtc, tag.UpdatedAtUtc, tag.Version, tag.IsHot, tag.IsRecommended
        """;

    // 同一SQL内按标签汇总；不逐标签发起查询，不统计其他租户的历史错误关联。
    private const string ReferenceCounts = """
        LEFT JOIN (
            SELECT assignment.TagId, COUNT(1) AS UseCount
            FROM fn_document_tag_assignment AS assignment
            INNER JOIN fn_document_item AS item ON item.Id = assignment.DocumentItemId
            WHERE item.TenantId IS NULL
            GROUP BY assignment.TagId
        ) AS reference_counts ON reference_counts.TagId = tag.Id
        """;

    public static readonly SqlStatement ListActive = new(
        "document.host_tag.list_active",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_tag AS tag
        {{ReferenceCounts}}
        WHERE tag.TenantId IS NULL AND tag.IsDeleted = 0
          AND (@IsHot IS NULL OR tag.IsHot = @IsHot)
          AND (@IsRecommended IS NULL OR tag.IsRecommended = @IsRecommended)
        ORDER BY tag.Name, tag.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveById = new(
        "document.host_tag.find_active_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_tag AS tag
        {{ReferenceCounts}}
        WHERE tag.Id = @Id AND tag.TenantId IS NULL AND tag.IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveByName = new(
        "document.host_tag.count_active_by_name",
        """
        SELECT COUNT(1)
        FROM fn_document_tag
        WHERE TenantId IS NULL AND IsDeleted = 0 AND Name = @Name
          AND (@ExcludeId IS NULL OR Id <> @ExcludeId)
        """,
        SqlDataScope.HostOnly);

    /// <summary>持标签写锁后只回读元数据，使用写入前的Host引用快照，避免与文档回读形成锁循环。</summary>
    public static readonly SqlStatement FindActiveByIdForMutationReload = new(
        "document.host_tag.find_active_by_id_for_mutation_reload",
        """
        SELECT Id, Name, Code, Icon, Color, Description, @UseCount AS UseCount,
            CreatedAtUtc, UpdatedAtUtc, Version, IsHot, IsRecommended
        FROM fn_document_tag
        WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
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
             IsHot, IsRecommended,
             IsDeleted, DeletedAtUtc, DeletedByUserId,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, NULL, @Name, @Code, @Icon, @Color, @Description, @UseCount,
             @IsHot, @IsRecommended,
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
            IsHot = @IsHot,
            IsRecommended = @IsRecommended,
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
