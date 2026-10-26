using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>选票附件单独持有引用，不能借用共享提交的外键或授权。</summary>
internal static class WorkflowVoteAttachmentSql
{
    public static readonly SqlStatement Insert = new("workflow.vote_attachment.insert", """
        INSERT INTO fn_workflow_vote_attachment (Id, BallotId, InstanceId, TenantScopeKey, FieldKey, FileId, OwnerResourceId, CreatedAtUtc)
        SELECT @Id, ballot.Id, ballot.InstanceId, instance.TenantScopeKey, @FieldKey, @FileId, @OwnerResourceId, @Now
        FROM fn_workflow_vote_ballot AS ballot
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=ballot.InstanceId
        WHERE ballot.Id=@BallotId AND ballot.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND ballot.TodoId=@TodoId AND ballot.AssigneeUserId=@ActorUserId AND ballot.StatusKey='submitted'
          AND ballot.Revision=@Revision AND instance.StatusKey='active'
        """, SqlDataScope.Global);

    public static readonly SqlStatement ExistsForProbe = new("workflow.vote_attachment.exists_for_probe", """
        SELECT COUNT(*) FROM fn_workflow_vote_attachment
        WHERE Id=@ReferenceId AND FileId=@FileId AND TenantScopeKey='host'
        """, SqlDataScope.Global);

    private const string OwnedFile = """
        FROM fn_workflow_vote_attachment AS attachment
        INNER JOIN fn_workflow_vote_ballot AS ballot ON ballot.Id=attachment.BallotId AND ballot.InstanceId=attachment.InstanceId
        INNER JOIN fn_workflow_vote_view AS privateView ON privateView.BallotId=ballot.Id AND privateView.InstanceId=attachment.InstanceId
        INNER JOIN fn_workflow_todo AS todo ON todo.Id=privateView.Id AND todo.InstanceId=attachment.InstanceId AND todo.StepId=ballot.VoteId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=attachment.InstanceId AND instance.TenantScopeKey=attachment.TenantScopeKey
        INNER JOIN fn_workflow_vote AS vote ON vote.Id=ballot.VoteId AND vote.InstanceId=instance.Id
        INNER JOIN fn_workflow_vote_form AS binding ON binding.Id=vote.FormBindingId AND binding.DefinitionVersionId=instance.DefinitionVersionId
        INNER JOIN fn_workflow_form_version AS formVersion ON formVersion.Id=binding.BallotFormVersionId
        INNER JOIN fn_workflow_form_definition AS formDefinition ON formDefinition.Id=formVersion.FormDefinitionId
        WHERE attachment.InstanceId=@InstanceId AND attachment.FileId=@FileId AND attachment.TenantScopeKey=@TenantScopeKey
          AND todo.AssigneeUserId=@ActorUserId AND privateView.OutputJson IS NOT NULL
          AND formDefinition.TenantScopeKey=@TenantScopeKey
        """;
    private const string OwnedOrder = " ORDER BY todo.ArrivedAtUtc DESC, todo.Id";
    // 先按本人历史Todo裁剪，再检查该次已提交的签名封套中是否实际含该文件。
    public static readonly SqlStatement ReadOwnedSqlServer = new("workflow.vote_attachment.read_owned.sqlserver",
        "SELECT TOP (128) attachment.OwnerResourceId, formVersion.FormSchemaJson, privateView.OutputJson " + OwnedFile + OwnedOrder, SqlDataScope.Global);
    public static readonly SqlStatement ReadOwnedMySql = new("workflow.vote_attachment.read_owned.mysql",
        "SELECT attachment.OwnerResourceId, formVersion.FormSchemaJson, privateView.OutputJson " + OwnedFile + OwnedOrder + " LIMIT 128", SqlDataScope.Global);
    public static readonly SqlStatement ReadOwnedForTodoSqlServer = new("workflow.vote_attachment.read_owned_for_todo.sqlserver",
        "SELECT TOP (1) attachment.OwnerResourceId, formVersion.FormSchemaJson, privateView.OutputJson " + OwnedFile + " AND todo.Id=@TodoId", SqlDataScope.Global);
    public static readonly SqlStatement ReadOwnedForTodoMySql = new("workflow.vote_attachment.read_owned_for_todo.mysql",
        "SELECT attachment.OwnerResourceId, formVersion.FormSchemaJson, privateView.OutputJson " + OwnedFile + " AND todo.Id=@TodoId LIMIT 1", SqlDataScope.Global);
}

/// <summary>本人历史选票及其实际资源所有者；不能使用当前成员答案替代私有视图。</summary>
internal sealed record WorkflowVoteAttachmentViewRecord(Guid? OwnerResourceId, string FormSchemaJson, string OutputJson);
