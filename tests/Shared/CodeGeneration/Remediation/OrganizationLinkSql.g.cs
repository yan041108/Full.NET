using Full.NET.Data.Abstractions;

namespace Acme.Remediation.Generated;

public static class OrganizationLinkSql
{
    /// <summary>按主键查询单条实体，含租户与软删除过滤。</summary>
    public const string FindById = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            SourceId,
            TargetId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_organization_link
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>统计实体总数，含租户与软删除过滤。</summary>
    public const string Count = """
        SELECT COUNT(1)
        FROM acme_catalog_organization_link
        WHERE TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>SQL Server 分页列表查询，按 Id 升序，含租户与软删除过滤。</summary>
    public const string ListSqlServer = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            SourceId,
            TargetId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_organization_link
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
        ORDER BY Id
        OFFSET @Offset ROWS
        FETCH NEXT @PageSize ROWS ONLY;
        """;

    /// <summary>MySQL 分页列表查询，按 Id 升序，含租户与软删除过滤。</summary>
    public const string ListMySql = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            SourceId,
            TargetId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_organization_link
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
        ORDER BY Id
        LIMIT @PageSize OFFSET @Offset;
        """;

    /// <summary>插入实体记录。</summary>
    public const string Insert = """
        INSERT INTO acme_catalog_organization_link (
            Id, TenantId, OrganizationUnitId, SourceId, TargetId, Version, IsDeleted, DeletedAtUtc, DeletedById)
        VALUES (
            @Id, @TenantId, @OrganizationUnitId, @SourceId, @TargetId, @Version, @IsDeleted, @DeletedAtUtc, @DeletedById);
        """;

    /// <summary>更新实体字段，含租户、乐观锁版本与软删除过滤。</summary>
    public const string Update = """
        UPDATE acme_catalog_organization_link
        SET SourceId = @SourceId,
            TargetId = @TargetId,
            Version = Version + 1
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND Version = @Version
            AND IsDeleted = 0;
        """;

    /// <summary>软删除实体（置 IsDeleted=1），含租户、乐观锁版本与软删除过滤。</summary>
    public const string Delete = """
        UPDATE acme_catalog_organization_link
        SET IsDeleted = 1,
            DeletedAtUtc = @DeletedAtUtc,
            DeletedById = @DeletedById,
            Version = Version + 1
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND Version = @Version
            AND IsDeleted = 0;
        """;

    public const string QueryCountSqlServer = """
        SELECT COUNT(1)
        FROM acme_catalog_organization_link
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_SourceId IS NULL OR SourceId = @Query_SourceId)
            AND (@Query_TargetId IS NULL OR TargetId = @Query_TargetId);
        """;
    public const string QueryListSqlServer = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            SourceId,
            TargetId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_organization_link
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_SourceId IS NULL OR SourceId = @Query_SourceId)
            AND (@Query_TargetId IS NULL OR TargetId = @Query_TargetId)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'sourceId' AND @CodegenDescending = 0 THEN SourceId END ASC,
            CASE WHEN @CodegenSortBy = 'sourceId' AND @CodegenDescending = 1 THEN SourceId END DESC,
            CASE WHEN @CodegenSortBy = 'targetId' AND @CodegenDescending = 0 THEN TargetId END ASC,
            CASE WHEN @CodegenSortBy = 'targetId' AND @CodegenDescending = 1 THEN TargetId END DESC,
            Id ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """;

    public static readonly SqlStatement QueryCountSqlServerStatement = new(
        "catalog.count_organization_links_query.sql_server",
        QueryCountSqlServer, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public const string QueryCountMySql = """
        SELECT COUNT(1)
        FROM acme_catalog_organization_link
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_SourceId IS NULL OR SourceId = @Query_SourceId)
            AND (@Query_TargetId IS NULL OR TargetId = @Query_TargetId);
        """;
    public const string QueryListMySql = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            SourceId,
            TargetId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_organization_link
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_SourceId IS NULL OR SourceId = @Query_SourceId)
            AND (@Query_TargetId IS NULL OR TargetId = @Query_TargetId)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'sourceId' AND @CodegenDescending = 0 THEN SourceId END ASC,
            CASE WHEN @CodegenSortBy = 'sourceId' AND @CodegenDescending = 1 THEN SourceId END DESC,
            CASE WHEN @CodegenSortBy = 'targetId' AND @CodegenDescending = 0 THEN TargetId END ASC,
            CASE WHEN @CodegenSortBy = 'targetId' AND @CodegenDescending = 1 THEN TargetId END DESC,
            Id ASC
        LIMIT @PageSize OFFSET @Offset;
        """;

    public static readonly SqlStatement QueryCountMySqlStatement = new(
        "catalog.count_organization_links_query.my_sql",
        QueryCountMySql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByIdStatement = new(
        "catalog.find_organization_link_by_id",
        FindById,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountStatement = new(
        "catalog.count_organization_links",
        Count,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListSqlServerStatement = new(
        "catalog.list_organization_links.sql_server.rows",
        QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMySqlStatement = new(
        "catalog.list_organization_links.my_sql.rows",
        QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServerStatement = new(
        "catalog.list_organization_links.sql_server",
        QueryCountSqlServer + "\n" + QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySqlStatement = new(
        "catalog.list_organization_links.my_sql",
        QueryCountMySql + "\n" + QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertStatement = new(
        "catalog.insert_organization_link",
        Insert,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateStatement = new(
        "catalog.update_organization_link",
        Update,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DeleteStatement = new(
        "catalog.delete_organization_link",
        Delete,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
