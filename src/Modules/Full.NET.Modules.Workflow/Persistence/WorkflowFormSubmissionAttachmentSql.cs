using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>工作流表单提交附件投影 SQL；供 claim 探测与运行时授权使用。</summary>
internal static class WorkflowFormSubmissionAttachmentSql
{
    public static readonly SqlStatement Insert = new(
        "workflow.form_submission_attachment.insert",
        """
        INSERT INTO fn_workflow_form_submission_attachment
            (Id, SubmissionId, InstanceId, FieldKey, FileId, TenantScopeKey, CreatedAtUtc)
        VALUES
            (@Id, @SubmissionId, @InstanceId, @FieldKey, @FileId, @TenantScopeKey, @CreatedAtUtc)
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
            WHERE SubmissionId = @SubmissionId
              AND FileId = @FileId
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.Global);
}
