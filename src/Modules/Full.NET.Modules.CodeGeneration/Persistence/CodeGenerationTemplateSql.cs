using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.CodeGeneration.Persistence;

internal static class CodeGenerationTemplateSql
{
    private const string Projection = """
        Id, Name, Description, SchemaJson, SchemaSha256,
        CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, Version
        """;

    public static readonly SqlStatement PageSqlServer = new(
        "codegen.template.page.sql_server",
        $$"""
        SELECT COUNT(1)
        FROM fn_codegeneration_template
        WHERE IsDeleted = 0;

        SELECT {{Projection}}
        FROM fn_codegeneration_template
        WHERE IsDeleted = 0
        ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageMySql = new(
        "codegen.template.page.my_sql",
        $$"""
        SELECT COUNT(1)
        FROM fn_codegeneration_template
        WHERE IsDeleted = 0;

        SELECT {{Projection}}
        FROM fn_codegeneration_template
        WHERE IsDeleted = 0
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
