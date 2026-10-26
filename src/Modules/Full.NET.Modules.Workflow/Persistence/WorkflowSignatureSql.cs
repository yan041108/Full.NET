using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>签名证据仅保存本模块动作绑定，文件内容和 claim 由 Files 独占。</summary>
internal static class WorkflowSignatureSql
{
    /// <summary>资源归属只从已提交证据裁定，不能信任 Files 传入的资源或文件声明。</summary>
    public static readonly SqlStatement ExistsForTenantOwner = new("workflow.action_evidence.exists_for_tenant_owner", """
        SELECT CASE WHEN EXISTS (SELECT 1 FROM fn_workflow_action_evidence AS evidence
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=evidence.InstanceId AND instance.TenantScopeKey=evidence.TenantScopeKey
        WHERE evidence.TenantScopeKey=@TenantScopeKey AND evidence.ActorUserId=@ActorUserId AND evidence.FileId=@FileId
        UNION ALL
        SELECT 1 FROM fn_workflow_form_submission_attachment AS attachment
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=attachment.InstanceId AND instance.TenantScopeKey=attachment.TenantScopeKey
        WHERE attachment.TenantScopeKey=@TenantScopeKey AND attachment.OwnerResourceId=@ActorUserId AND attachment.FileId=@FileId
        UNION ALL
        SELECT 1 FROM fn_workflow_vote_attachment AS attachment
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=attachment.InstanceId AND instance.TenantScopeKey=attachment.TenantScopeKey
        WHERE attachment.TenantScopeKey=@TenantScopeKey AND attachment.OwnerResourceId=@ActorUserId AND attachment.FileId=@FileId)
        THEN 1 ELSE 0 END
        """, SqlDataScope.Global);
    private const string Projection = "evidence.Id, evidence.ActorUserId, evidence.OriginalAssigneeUserId, evidence.ActionKey, evidence.FormVersionId, evidence.FormRevision, evidence.SubmissionHash, evidence.FileId, evidence.FileHash, evidence.CreatedAtUtc";
    private const string OwnedEvidence = """
        FROM fn_workflow_action_evidence AS evidence
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=evidence.InstanceId
        INNER JOIN fn_workflow_todo AS todo ON todo.Id=evidence.TodoId AND todo.InstanceId=instance.Id
        WHERE evidence.TodoId=@TodoId AND evidence.TenantScopeKey=@TenantScopeKey AND instance.TenantScopeKey=@TenantScopeKey
          AND (todo.AssigneeUserId=@ActorUserId OR evidence.ActorUserId=@ActorUserId)
        """;
    public static readonly SqlStatement ListSqlServer = new("workflow.action_evidence.list.sqlserver",
        "SELECT TOP (20) " + Projection + " " + OwnedEvidence + " ORDER BY evidence.CreatedAtUtc DESC, evidence.Id", SqlDataScope.Global);
    public static readonly SqlStatement ListMySql = new("workflow.action_evidence.list.mysql",
        "SELECT " + Projection + " " + OwnedEvidence + " ORDER BY evidence.CreatedAtUtc DESC, evidence.Id LIMIT 20", SqlDataScope.Global);
    public static readonly SqlStatement FindOwned = new("workflow.action_evidence.find_owned",
        "SELECT " + Projection + " " + OwnedEvidence + " AND evidence.Id=@Id", SqlDataScope.Global);
    public static readonly SqlStatement CanReadTodo = new("workflow.action_evidence.can_read_todo", """
        SELECT COUNT(*) FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
        WHERE todo.Id=@TodoId AND instance.TenantScopeKey=@TenantScopeKey
          AND (todo.AssigneeUserId=@ActorUserId OR EXISTS (SELECT 1 FROM fn_workflow_action_evidence AS evidence
              WHERE evidence.TodoId=todo.Id AND evidence.ActorUserId=@ActorUserId AND evidence.TenantScopeKey=@TenantScopeKey))
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.action_evidence.insert", """
        INSERT INTO fn_workflow_action_evidence
            (Id, InstanceId, StepId, TodoId, TenantScopeKey, ActorUserId, OriginalAssigneeUserId,
             ActionKey, IdempotencyKey, FormVersionId, FormRevision, SubmissionHash, FileId, FileHash, CreatedAtUtc)
        VALUES (@Id, @InstanceId, @StepId, @TodoId, @TenantScopeKey, @ActorUserId, @OriginalAssigneeUserId,
            @ActionKey, @IdempotencyKey, @FormVersionId, @FormRevision, @SubmissionHash, @FileId, @FileHash, @CreatedAtUtc)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Exists = new("workflow.action_evidence.exists", """
        SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE Id=@Id AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
}

/// <summary>签名证据的静态数据库读取投影，不能作为跨模块文件访问凭据。</summary>
internal sealed record WorkflowSignatureEvidenceRecord(Guid Id, Guid ActorUserId, Guid OriginalAssigneeUserId,
    string ActionKey, Guid FormVersionId, long FormRevision, string SubmissionHash, Guid FileId,
    string FileHash, DateTimeOffset CreatedAtUtc);
