using Full.NET.Data.Abstractions;

namespace Acme.Remediation.Generated;

public static class ProductTagSql
{
    /// <summary>按主键查询单条实体，含租户与软删除过滤。</summary>
    public const string FindById = """
        SELECT
            Id,
            TenantId,
            ProductId AS ProductReferenceId,
            TagId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_product_tag
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>统计实体总数，含租户与软删除过滤。</summary>
    public const string Count = """
        SELECT COUNT(1)
        FROM acme_catalog_product_tag
        WHERE TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>SQL Server 分页列表查询，按 Id 升序，含租户与软删除过滤。</summary>
    public const string ListSqlServer = """
        SELECT
            Id,
            TenantId,
            ProductId AS ProductReferenceId,
            TagId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_product_tag
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
            ProductId AS ProductReferenceId,
            TagId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_product_tag
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
        ORDER BY Id
        LIMIT @PageSize OFFSET @Offset;
        """;

    /// <summary>插入实体记录。</summary>
    public const string Insert = """
        INSERT INTO acme_catalog_product_tag (
            Id, TenantId, ProductId, TagId, Version, IsDeleted, DeletedAtUtc, DeletedById)
        VALUES (
            @Id, @TenantId, @ProductId, @TagId, @Version, @IsDeleted, @DeletedAtUtc, @DeletedById);
        """;

    /// <summary>更新实体字段，含租户、乐观锁版本与软删除过滤。</summary>
    public const string Update = """
        UPDATE acme_catalog_product_tag
        SET ProductId = @ProductId,
            TagId = @TagId,
            Version = Version + 1
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND Version = @Version
            AND IsDeleted = 0;
        """;

    /// <summary>软删除实体（置 IsDeleted=1），含租户、乐观锁版本与软删除过滤。</summary>
    public const string Delete = """
        UPDATE acme_catalog_product_tag
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
        FROM acme_catalog_product_tag
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_ProductId IS NULL OR ProductId = @Query_ProductId)
            AND (@Query_TagId IS NULL OR TagId = @Query_TagId);
        """;
    public const string QueryListSqlServer = """
        SELECT
            Id,
            TenantId,
            ProductId AS ProductReferenceId,
            TagId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_product_tag
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_ProductId IS NULL OR ProductId = @Query_ProductId)
            AND (@Query_TagId IS NULL OR TagId = @Query_TagId)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'productReferenceId' AND @CodegenDescending = 0 THEN ProductId END ASC,
            CASE WHEN @CodegenSortBy = 'productReferenceId' AND @CodegenDescending = 1 THEN ProductId END DESC,
            CASE WHEN @CodegenSortBy = 'tagId' AND @CodegenDescending = 0 THEN TagId END ASC,
            CASE WHEN @CodegenSortBy = 'tagId' AND @CodegenDescending = 1 THEN TagId END DESC,
            Id ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """;

    public static readonly SqlStatement QueryCountSqlServerStatement = new(
        "catalog.count_product_tags_query.sql_server",
        QueryCountSqlServer, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public const string QueryCountMySql = """
        SELECT COUNT(1)
        FROM acme_catalog_product_tag
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_ProductId IS NULL OR ProductId = @Query_ProductId)
            AND (@Query_TagId IS NULL OR TagId = @Query_TagId);
        """;
    public const string QueryListMySql = """
        SELECT
            Id,
            TenantId,
            ProductId AS ProductReferenceId,
            TagId,
            Version,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM acme_catalog_product_tag
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_ProductId IS NULL OR ProductId = @Query_ProductId)
            AND (@Query_TagId IS NULL OR TagId = @Query_TagId)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'productReferenceId' AND @CodegenDescending = 0 THEN ProductId END ASC,
            CASE WHEN @CodegenSortBy = 'productReferenceId' AND @CodegenDescending = 1 THEN ProductId END DESC,
            CASE WHEN @CodegenSortBy = 'tagId' AND @CodegenDescending = 0 THEN TagId END ASC,
            CASE WHEN @CodegenSortBy = 'tagId' AND @CodegenDescending = 1 THEN TagId END DESC,
            Id ASC
        LIMIT @PageSize OFFSET @Offset;
        """;

    public static readonly SqlStatement QueryCountMySqlStatement = new(
        "catalog.count_product_tags_query.my_sql",
        QueryCountMySql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByIdStatement = new(
        "catalog.find_product_tag_by_id",
        FindById,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountStatement = new(
        "catalog.count_product_tags",
        Count,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListSqlServerStatement = new(
        "catalog.list_product_tags.sql_server.rows",
        QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMySqlStatement = new(
        "catalog.list_product_tags.my_sql.rows",
        QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServerStatement = new(
        "catalog.list_product_tags.sql_server",
        QueryCountSqlServer + "\n" + QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySqlStatement = new(
        "catalog.list_product_tags.my_sql",
        QueryCountMySql + "\n" + QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertStatement = new(
        "catalog.insert_product_tag",
        Insert,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateStatement = new(
        "catalog.update_product_tag",
        Update,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DeleteStatement = new(
        "catalog.delete_product_tag",
        Delete,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
