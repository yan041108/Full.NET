using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 定义 Host 代码生成运行摘要及 Apply 单向终态收敛的 SQL 边界。
/// </summary>
internal static class CodeGenerationRunSql
{
    private const string Projection = """
        Id, TemplateId, TemplateVersion, OperationKind, Status,
        ModuleKey, EntityKey, SchemaSha256, ArtifactCount, ManifestSha256,
        ErrorCode, RequestedByUserId, StartedAtUtc, FinishedAtUtc
        """;

    public static readonly SqlStatement Insert = new(
        "codegen.run.insert",
        """
        INSERT INTO fn_codegeneration_run
            (Id, TemplateId, TemplateVersion, OperationKind, Status,
             ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
             ManifestSha256, ErrorCode, RequestedByUserId,
             StartedAtUtc, FinishedAtUtc)
        VALUES
            (@Id, @TemplateId, @TemplateVersion, @OperationKind, @Status,
             @ModuleKey, @EntityKey, @SchemaSha256, @ArtifactCount,
             @ManifestSha256, @ErrorCode, @RequestedByUserId,
             @StartedAtUtc, @FinishedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "codegen.run.find_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_codegeneration_run
        WHERE Id = @Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CompleteApply = new(
        "codegen.run.complete_apply",
        """
        UPDATE fn_codegeneration_run
        SET Status = 'succeeded',
            FinishedAtUtc = @FinishedAtUtc
        WHERE Id = @Id
          AND OperationKind = 'apply'
          AND Status = 'running'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FailApply = new(
        "codegen.run.fail_apply",
        """
        UPDATE fn_codegeneration_run
        SET Status = 'failed',
            ModuleKey = NULL,
            EntityKey = NULL,
            SchemaSha256 = NULL,
            ArtifactCount = 0,
            ManifestSha256 = NULL,
            ErrorCode = @ErrorCode,
            FinishedAtUtc = @FinishedAtUtc
        WHERE Id = @Id
          AND OperationKind = 'apply'
          AND Status = 'running'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageSqlServer = new(
        "codegen.run.page.sql_server",
        $$"""
        SELECT COUNT(1)
        FROM fn_codegeneration_run
        WHERE (@Status IS NULL OR Status = @Status);

        SELECT {{Projection}}
        FROM fn_codegeneration_run
        WHERE (@Status IS NULL OR Status = @Status)
        ORDER BY StartedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageMySql = new(
        "codegen.run.page.my_sql",
        $$"""
        SELECT COUNT(1)
        FROM fn_codegeneration_run
        WHERE (@Status IS NULL OR Status = @Status);

        SELECT {{Projection}}
        FROM fn_codegeneration_run
        WHERE (@Status IS NULL OR Status = @Status)
        ORDER BY StartedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
