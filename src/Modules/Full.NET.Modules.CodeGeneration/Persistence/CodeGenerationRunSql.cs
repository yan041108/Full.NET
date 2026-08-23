using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 定义 Host 代码生成运行摘要及 Apply/Rollback 单向终态收敛的 SQL 边界。
/// </summary>
internal static class CodeGenerationRunSql
{
    private const string Projection = """
        Id, TemplateId, TemplateVersion, OperationKind, Status,
        ModuleKey, EntityKey, SchemaSha256, ArtifactCount, ManifestSha256,
        ErrorCode, RequestedByUserId, StartedAtUtc, FinishedAtUtc,
        SourceApplyRunId
        """;

    public static readonly SqlStatement Insert = new(
        "codegen.run.insert",
        """
        INSERT INTO fn_codegeneration_run
            (Id, TemplateId, TemplateVersion, OperationKind, Status,
             ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
             ManifestSha256, ErrorCode, RequestedByUserId,
             StartedAtUtc, FinishedAtUtc, SourceApplyRunId)
        VALUES
            (@Id, @TemplateId, @TemplateVersion, @OperationKind, @Status,
             @ModuleKey, @EntityKey, @SchemaSha256, @ArtifactCount,
             @ManifestSha256, @ErrorCode, @RequestedByUserId,
             @StartedAtUtc, @FinishedAtUtc, @SourceApplyRunId)
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

    public static readonly SqlStatement FindSucceededRollbackBySourceApplyRunId = new(
        "codegen.run.find_succeeded_rollback_by_source",
        $$"""
        SELECT {{Projection}}
        FROM fn_codegeneration_run
        WHERE SourceApplyRunId = @SourceApplyRunId
          AND OperationKind = 'rollback'
          AND Status = 'succeeded'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindRunningRollbackBySourceApplyRunId = new(
        "codegen.run.find_running_rollback_by_source",
        $$"""
        SELECT {{Projection}}
        FROM fn_codegeneration_run
        WHERE SourceApplyRunId = @SourceApplyRunId
          AND OperationKind = 'rollback'
          AND Status = 'running'
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按模块/实体列出已成功且尚未被成功回滚的 Apply（LIFO 投影）；rollback-chain 校验顺序以此结果集为权威，确保回滚不跳过较新的 Apply 而破坏一致性。
    /// </summary>
    public static readonly SqlStatement ListPendingRollbackApplies = new(
        "codegen.run.list_pending_rollback_applies",
        """
        SELECT a.Id
        FROM fn_codegeneration_run a
        WHERE a.OperationKind = 'apply'
          AND a.Status = 'succeeded'
          AND a.ModuleKey = @ModuleKey
          AND a.EntityKey = @EntityKey
          AND NOT EXISTS (
              SELECT 1
              FROM fn_codegeneration_run r
              WHERE r.SourceApplyRunId = a.Id
                AND r.OperationKind = 'rollback'
                AND r.Status = 'succeeded')
        ORDER BY a.StartedAtUtc DESC, a.Id
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

    /// <summary>
    /// 将 running Apply 收敛为 failed 并清空 ModuleKey/EntityKey/Schema/Manifest 等字段，仅匹配 running 行避免重复终态；失败后不得保留可被误用的摘要。
    /// </summary>
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

    public static readonly SqlStatement CompleteRollback = new(
        "codegen.run.complete_rollback",
        """
        UPDATE fn_codegeneration_run
        SET Status = 'succeeded',
            FinishedAtUtc = @FinishedAtUtc
        WHERE Id = @Id
          AND OperationKind = 'rollback'
          AND Status = 'running'
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 将 running Rollback 收敛为 failed 并清空敏感字段，仅匹配 running 行避免重复终态；与 FailApply 对称，防止失败回滚残留可被误用的摘要。
    /// </summary>
    public static readonly SqlStatement FailRollback = new(
        "codegen.run.fail_rollback",
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
          AND OperationKind = 'rollback'
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

    public static readonly SqlStatement ListEligibleCheckpointCleanupSqlServer = new(
        "codegen.run.list_eligible_checkpoint_cleanup.sql_server",
        """
        SELECT TOP (@Take) a.Id AS ApplyRunId
        FROM fn_codegeneration_run a
        INNER JOIN fn_codegeneration_run r
            ON r.SourceApplyRunId = a.Id
        WHERE a.OperationKind = 'apply'
          AND a.Status = 'succeeded'
          AND r.OperationKind = 'rollback'
          AND r.Status = 'succeeded'
          AND r.FinishedAtUtc <= @CutoffUtc
        ORDER BY r.FinishedAtUtc ASC, a.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListEligibleCheckpointCleanupMySql = new(
        "codegen.run.list_eligible_checkpoint_cleanup.my_sql",
        """
        SELECT a.Id AS ApplyRunId
        FROM fn_codegeneration_run a
        INNER JOIN fn_codegeneration_run r
            ON r.SourceApplyRunId = a.Id
        WHERE a.OperationKind = 'apply'
          AND a.Status = 'succeeded'
          AND r.OperationKind = 'rollback'
          AND r.Status = 'succeeded'
          AND r.FinishedAtUtc <= @CutoffUtc
        ORDER BY r.FinishedAtUtc ASC, a.Id
        LIMIT @Take
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListCapacityOverflowCheckpointCleanupSqlServer = new(
        "codegen.run.list_capacity_overflow_checkpoint_cleanup.sql_server",
        """
        SELECT TOP (@Take) a.Id AS ApplyRunId
        FROM fn_codegeneration_run a
        INNER JOIN fn_codegeneration_run r
            ON r.SourceApplyRunId = a.Id
        WHERE a.OperationKind = 'apply'
          AND a.Status = 'succeeded'
          AND r.OperationKind = 'rollback'
          AND r.Status = 'succeeded'
        ORDER BY r.FinishedAtUtc ASC, a.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListCapacityOverflowCheckpointCleanupMySql = new(
        "codegen.run.list_capacity_overflow_checkpoint_cleanup.my_sql",
        """
        SELECT a.Id AS ApplyRunId
        FROM fn_codegeneration_run a
        INNER JOIN fn_codegeneration_run r
            ON r.SourceApplyRunId = a.Id
        WHERE a.OperationKind = 'apply'
          AND a.Status = 'succeeded'
          AND r.OperationKind = 'rollback'
          AND r.Status = 'succeeded'
        ORDER BY r.FinishedAtUtc ASC, a.Id
        LIMIT @Take
        """,
        SqlDataScope.HostOnly);
}