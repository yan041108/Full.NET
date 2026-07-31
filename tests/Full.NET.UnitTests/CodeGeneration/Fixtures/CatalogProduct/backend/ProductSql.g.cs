using Full.NET.Data.Abstractions;

namespace Acme.Modules.Catalog.Generated;

public static class ProductSql
{
    public const string FindById = """
        SELECT
            Id,
            TenantId,
            Name,
            Description,
            IsActive,
            Version,
            CreatedAtUtc
        FROM acme_catalog_product
        WHERE Id = @Id
            AND TenantId = @TenantId;
        """;

    public const string Count = """
        SELECT COUNT(1)
        FROM acme_catalog_product
        WHERE TenantId = @TenantId;
        """;

    public const string ListSqlServer = """
        SELECT
            Id,
            TenantId,
            Name,
            Description,
            IsActive,
            Version,
            CreatedAtUtc
        FROM acme_catalog_product
        WHERE 1 = 1
            AND TenantId = @TenantId
        ORDER BY Id
        OFFSET @Offset ROWS
        FETCH NEXT @PageSize ROWS ONLY;
        """;

    public const string ListMySql = """
        SELECT
            Id,
            TenantId,
            Name,
            Description,
            IsActive,
            Version,
            CreatedAtUtc
        FROM acme_catalog_product
        WHERE 1 = 1
            AND TenantId = @TenantId
        ORDER BY Id
        LIMIT @PageSize OFFSET @Offset;
        """;

    public const string Insert = """
        INSERT INTO acme_catalog_product (
            Id, TenantId, Name, Description, IsActive, Version, CreatedAtUtc)
        VALUES (
            @Id, @TenantId, @Name, @Description, @IsActive, @Version, @CreatedAtUtc);
        """;

    public const string Update = """
        UPDATE acme_catalog_product
        SET Name = @Name,
            Description = @Description,
            IsActive = @IsActive,
            Version = Version + 1
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND Version = @Version;
        """;

    public const string Disable = """
        UPDATE acme_catalog_product
        SET IsActive = 0,
            Version = Version + 1
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND Version = @Version;
        """;

    public static readonly SqlStatement FindByIdStatement = new(
        "catalog.find_product_by_id",
        FindById,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServerStatement = new(
        "catalog.list_products.sql_server",
        Count + "\n" + ListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySqlStatement = new(
        "catalog.list_products.my_sql",
        Count + "\n" + ListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertStatement = new(
        "catalog.insert_product",
        Insert,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateStatement = new(
        "catalog.update_product",
        Update,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableStatement = new(
        "catalog.disable_product",
        Disable,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
