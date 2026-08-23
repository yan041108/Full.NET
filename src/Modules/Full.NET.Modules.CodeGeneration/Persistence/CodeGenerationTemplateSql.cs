using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// Host 代码生成模板表的 SQL 边界：所有语句均为 HostOnly 作用域、参数化；分页提供 SQL Server/MySQL 成对实现，软删除与乐观并发通过 Version 守卫。
/// </summary>
internal static class CodeGenerationTemplateSql
{
    private const string Projection = """
        Id, Name, Description, SchemaJson, SchemaSha256,
        CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, Version
        """;

    /// <summary>
    /// 列表筛选：名称模糊与 Schema JSON 内 databaseTableName 模糊；空参数表示不过滤。
    /// </summary>
    private const string PageWhereSqlServer = """
        IsDeleted = 0
          AND (@NameContains IS NULL OR Name LIKE '%' + @NameContains + '%')
          AND (@TableNameContains IS NULL
               OR JSON_VALUE(SchemaJson, '$.databaseTableName') LIKE '%' + @TableNameContains + '%')
        """;

    private const string PageWhereMySql = """
        IsDeleted = 0
          AND (@NameContains IS NULL OR Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@TableNameContains IS NULL
               OR JSON_UNQUOTE(JSON_EXTRACT(SchemaJson, '$.databaseTableName'))
                  LIKE CONCAT('%', @TableNameContains, '%'))
        """;

    public static readonly SqlStatement PageSqlServer = new(
        "codegen.template.page.sql_server",
        $$"""
        SELECT COUNT(1)
        FROM fn_codegeneration_template
        WHERE {{PageWhereSqlServer}};

        SELECT {{Projection}}
        FROM fn_codegeneration_template
        WHERE {{PageWhereSqlServer}}
        ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageMySql = new(
        "codegen.template.page.my_sql",
        $$"""
        SELECT COUNT(1)
        FROM fn_codegeneration_template
        WHERE {{PageWhereMySql}};

        SELECT {{Projection}}
        FROM fn_codegeneration_template
        WHERE {{PageWhereMySql}}
        ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "codegen.template.find_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_codegeneration_template
        WHERE Id = @Id
          AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "codegen.template.insert",
        """
        INSERT INTO fn_codegeneration_template
            (Id, Name, Description, SchemaJson, SchemaSha256,
             CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
             DeletedAtUtc, DeletedByUserId, IsDeleted, Version)
        VALUES
            (@Id, @Name, @Description, @SchemaJson, @SchemaSha256,
             @CreatedAtUtc, @CreatedByUserId, NULL, NULL,
             NULL, NULL, 0, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "codegen.template.update",
        """
        UPDATE fn_codegeneration_template
        SET Name = @Name,
            Description = @Description,
            SchemaJson = @SchemaJson,
            SchemaSha256 = @SchemaSha256,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
          AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SoftDelete = new(
        "codegen.template.soft_delete",
        """
        UPDATE fn_codegeneration_template
        SET DeletedAtUtc = @DeletedAtUtc,
            DeletedByUserId = @DeletedByUserId,
            IsDeleted = 1,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
          AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);
}
