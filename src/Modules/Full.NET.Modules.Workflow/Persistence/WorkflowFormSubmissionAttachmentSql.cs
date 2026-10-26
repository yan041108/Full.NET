using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>工作流表单提交附件投影 SQL；供 claim 探测与运行时授权使用。</summary>
internal static class WorkflowFormSubmissionAttachmentSql
{
    private const string ReadViewColumnsAndJoins = """
        step.NodeKey,
        CASE WHEN todo.StatusKey='active' THEN CASE WHEN step.ParallelJoinId IS NULL THEN submission.SubmissionJson ELSE branch.InputJson END
             ELSE viewRecord.InputJson END AS InputJson,
        CASE WHEN todo.StatusKey='active' AND step.ParallelJoinId IS NOT NULL THEN branch.OutputJson ELSE viewRecord.OutputJson END AS OutputJson
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId AND step.InstanceId=todo.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
        INNER JOIN fn_workflow_form_submission AS submission ON submission.InstanceId=instance.Id
        INNER JOIN fn_workflow_todo_view AS viewRecord ON viewRecord.Id=todo.Id AND viewRecord.InstanceId=instance.Id AND viewRecord.TenantScopeKey=instance.TenantScopeKey
        LEFT JOIN fn_workflow_parallel_join AS fork ON fork.Id=step.ParallelJoinId AND fork.InstanceId=instance.Id
        LEFT JOIN fn_workflow_branch_state AS branch ON branch.ParallelJoinId=fork.Id AND branch.BranchKey=step.ParallelBranchKey
        """;
    private const string BranchViewFilter = """
          AND (todo.StatusKey<>'active' OR step.ParallelJoinId IS NULL OR branch.BranchKey IS NOT NULL)
        """;
    private const string ReadViews = ReadViewColumnsAndJoins + " WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND todo.AssigneeUserId=@ActorUserId "
        + BranchViewFilter + " ORDER BY todo.ArrivedAtUtc DESC, todo.Id";
    public static readonly SqlStatement ReadViewsSqlServer = new("workflow.form_attachment.read_views.sqlserver",
        "SELECT TOP (128) " + ReadViews, SqlDataScope.Global);
    public static readonly SqlStatement ReadViewsMySql = new("workflow.form_attachment.read_views.mysql",
        "SELECT " + ReadViews + " LIMIT 128", SqlDataScope.Global);
    private const string ReadTodoView = "instance.Id AS InstanceId, instance.DefinitionVersionId, " + ReadViewColumnsAndJoins
        + " WHERE todo.Id=@TodoId AND instance.TenantScopeKey=@TenantScopeKey AND todo.AssigneeUserId=@ActorUserId " + BranchViewFilter;
    public static readonly SqlStatement ReadTodoViewSqlServer = new("workflow.form_attachment.read_todo_view.sqlserver",
        "SELECT TOP (1) " + ReadTodoView, SqlDataScope.Global);
    public static readonly SqlStatement ReadTodoViewMySql = new("workflow.form_attachment.read_todo_view.mysql",
        "SELECT " + ReadTodoView + " LIMIT 1", SqlDataScope.Global);

    /// <summary>按一个字段最多十个文件读取已持有的精确引用，不扫描实例全部历史。</summary>
    public static readonly SqlStatement ListForFieldFiles = new("workflow.form_attachment.list_for_field_files", """
        SELECT Id, FieldKey, FileId, OwnerResourceId FROM fn_workflow_form_submission_attachment
        WHERE SubmissionId=@SubmissionId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND FieldKey=@FieldKey AND FileId IN @FileIds
        """, SqlDataScope.Global);

    public static readonly SqlStatement ExistsForFieldFile = new("workflow.form_attachment.exists_for_field_file", """
        SELECT COUNT(*) FROM fn_workflow_form_submission_attachment
        WHERE SubmissionId=@SubmissionId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND FieldKey=@FieldKey AND FileId=@FileId
        """, SqlDataScope.Global);

    /// <summary>租户文件只能按本实例持久化的唯一资源所有者读取，不接受外部指定资源身份。</summary>
    public static readonly SqlStatement FindFileOwner = new("workflow.form_attachment.find_file_owner", """
        SELECT DISTINCT OwnerResourceId FROM fn_workflow_form_submission_attachment
        WHERE InstanceId=@InstanceId AND FileId=@FileId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    /// <summary>父子投影只承认明确映射的原字段历史引用，不能用同实例其他隐藏字段的文件授权替代。</summary>
    public static readonly SqlStatement FindMappedFileOwner = new("workflow.form_attachment.find_mapped_file_owner", """
        SELECT attachment.FieldKey,attachment.OwnerResourceId FROM fn_workflow_form_submission_attachment AS attachment
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=attachment.InstanceId
        WHERE attachment.InstanceId=@InstanceId AND attachment.FieldKey=@FieldKey AND attachment.FileId=@FileId
            AND attachment.TenantScopeKey=@TenantScopeKey AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new(
        "workflow.form_submission_attachment.insert",
        """
        INSERT INTO fn_workflow_form_submission_attachment
            (Id, SubmissionId, InstanceId, FieldKey, FileId, TenantScopeKey, CreatedAtUtc, ClaimReferenceId, OwnerResourceId)
        VALUES
            (@Id, @SubmissionId, @InstanceId, @FieldKey, @FileId, @TenantScopeKey, @CreatedAtUtc, @ClaimReferenceId, @OwnerResourceId)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement DeleteBySubmissionFieldFile = new(
        "workflow.form_submission_attachment.delete_by_submission_field_file",
        """
        DELETE FROM fn_workflow_form_submission_attachment
        WHERE SubmissionId = @SubmissionId
          AND FieldKey = @FieldKey
          AND FileId = @FileId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ExistsForSubmissionFile = new(
        "workflow.form_submission_attachment.exists_for_submission_file",
        """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM fn_workflow_form_submission_attachment
            WHERE SubmissionId = @SubmissionId
              AND FileId = @FileId
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ExistsForInstanceFile = new(
        "workflow.form_submission_attachment.exists_for_instance_file",
        """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM fn_workflow_form_submission_attachment AS attachment
            INNER JOIN fn_workflow_instance AS instance ON instance.Id = attachment.InstanceId
            WHERE attachment.InstanceId = @InstanceId
              AND attachment.FileId = @FileId
              AND instance.TenantScopeKey = @TenantScopeKey
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ExistsForProbe = new(
        "workflow.form_submission_attachment.exists_for_probe",
        """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM fn_workflow_form_submission_attachment
            WHERE ClaimReferenceId = @ReferenceId AND TenantScopeKey = 'host' AND FileId = @FileId
            UNION ALL
            SELECT 1 FROM fn_workflow_action_evidence WHERE Id = @ReferenceId AND FileId = @FileId AND TenantScopeKey = 'host'
            UNION ALL
            SELECT 1 FROM fn_workflow_vote_attachment WHERE Id = @ReferenceId AND FileId = @FileId AND TenantScopeKey = 'host'
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.Global);
}

/// <summary>一次批量读取本人节点及其实际输入输出视图，文件授权不借用兄弟分支数据。</summary>
internal sealed record WorkflowAttachmentReadViewRecord(string NodeKey, string InputJson, string? OutputJson);

/// <summary>个人附件入口只持有一个实际 Todo 的视图与所属版本，不能扩展成同实例其他活动的读取权。</summary>
internal sealed record WorkflowTodoAttachmentReadViewRecord(Guid InstanceId, Guid DefinitionVersionId, string NodeKey, string InputJson, string? OutputJson);

/// <summary>精确附件历史引用及 Files 资源归属。</summary>
internal sealed record WorkflowAttachmentReferenceRecord(Guid Id, string FieldKey, Guid FileId, Guid? OwnerResourceId);

/// <summary>同一实例同一文件的唯一持久化资源所有者。</summary>
internal sealed record WorkflowAttachmentFileOwnerRecord(Guid? OwnerResourceId);

/// <summary>保留原字段身份供Ordinal复核，避免旧数据库排序规则把隐藏同名大小写字段当作授权来源。</summary>
internal sealed record WorkflowMappedAttachmentFileOwnerRecord(string FieldKey, Guid? OwnerResourceId);
