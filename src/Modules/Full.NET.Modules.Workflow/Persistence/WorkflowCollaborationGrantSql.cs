using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>显式双边控制面只允许当前可信参与方；业务实例和待办仍使用各自租户过滤。</summary>
internal static class WorkflowCollaborationGrantSql
{
    public static readonly SqlStatement Find=new("workflow.collaboration_grant.find","""
        SELECT grantRow.Id,grantRow.SourceTenantId,grantRow.TargetTenantId,grantRow.SourceTenantScopeKey,grantRow.TargetTenantScopeKey,grantRow.Revision,grantRow.StatusKey,grantRow.SourceActorId,grantRow.TargetActorId,grantRow.OfferJson,grantRow.AcceptanceJson,grantRow.PolicyHash,grantRow.ExpiresAtUtc,grantRow.CreatedAtUtc,grantRow.AcceptedAtUtc,grantRow.RevokedAtUtc,grantRow.RevokedById,grantRow.RevokedByScopeKey,grantRow.SourceDefinitionId,grantRow.SourceFormVersionId,grantRow.TargetDefinitionVersionId,grantRow.TargetSummaryJson,grantRow.TargetSummaryHash
         FROM fn_workflow_collaboration_grant AS grantRow WHERE grantRow.Id=@Id
         AND (grantRow.SourceTenantScopeKey=@TenantScopeKey OR grantRow.TargetTenantScopeKey=@TenantScopeKey)
        """,SqlDataScope.Global);
    public static readonly SqlStatement FindAction=new("workflow.collaboration_grant.find_action","""
        SELECT actionRow.RequestHash,actionRow.ResponseJson FROM fn_workflow_collaboration_grant_action AS actionRow
         INNER JOIN fn_workflow_collaboration_grant AS grantRow ON grantRow.Id=actionRow.GrantId
         WHERE grantRow.Id=@Id AND (grantRow.SourceTenantScopeKey=@TenantScopeKey OR grantRow.TargetTenantScopeKey=@TenantScopeKey)
         AND actionRow.ActorTenantScopeKey=@TenantScopeKey AND actionRow.ActorUserId=@ActorUserId AND actionRow.IdempotencyKey=@IdempotencyKey
        """,SqlDataScope.Global);
    public static readonly SqlStatement FindProposalAction=new("workflow.collaboration_grant.find_proposal_action","""
        SELECT actionRow.RequestHash,actionRow.ResponseJson FROM fn_workflow_collaboration_grant AS grantRow
         INNER JOIN fn_workflow_collaboration_grant_action AS actionRow ON actionRow.GrantId=grantRow.Id
         WHERE grantRow.SourceTenantScopeKey=@TenantScopeKey AND grantRow.SourceActorId=@ActorUserId AND grantRow.ProposalKey=@IdempotencyKey
         AND actionRow.ActorTenantScopeKey=@TenantScopeKey AND actionRow.ActorUserId=@ActorUserId AND actionRow.IdempotencyKey=@IdempotencyKey
         AND actionRow.OperationKey='propose'
        """,SqlDataScope.Global);
    public static readonly SqlStatement Insert=new("workflow.collaboration_grant.insert","""
        INSERT INTO fn_workflow_collaboration_grant (Id,SourceTenantId,TargetTenantId,SourceTenantScopeKey,TargetTenantScopeKey,
         SourceDefinitionId,SourceFormVersionId,SourceActorId,OfferJson,ProposalKey,ProposalHash,Revision,StatusKey,ExpiresAtUtc,CreatedAtUtc)
         SELECT @Id,@SourceTenantId,@TargetTenantId,@TenantScopeKey,@TargetTenantScopeKey,definition.Id,formVersion.Id,@ActorUserId,
         @OfferJson,@IdempotencyKey,@RequestHash,1,'proposed',@ExpiresAtUtc,@Now
         FROM fn_workflow_definition AS definition
         INNER JOIN fn_workflow_form_version AS formVersion ON formVersion.Id=@SourceFormVersionId
         INNER JOIN fn_workflow_form_definition AS formDefinition ON formDefinition.Id=formVersion.FormDefinitionId
         WHERE definition.Id=@SourceDefinitionId AND definition.TenantScopeKey=@TenantScopeKey AND definition.StatusKey='active'
         AND formDefinition.TenantScopeKey=@TenantScopeKey AND formDefinition.StatusKey='active'
        """,SqlDataScope.Global);
    public static readonly SqlStatement Accept=new("workflow.collaboration_grant.accept","""
        UPDATE fn_workflow_collaboration_grant SET TargetActorId=@ActorUserId,TargetDefinitionVersionId=@TargetDefinitionVersionId,
         AcceptanceJson=@AcceptanceJson,PolicyHash=@PolicyHash,TargetSummaryJson=@TargetSummaryJson,TargetSummaryHash=@TargetSummaryHash,
         StatusKey='active',Revision=Revision+1,AcceptedAtUtc=@Now
         WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND Revision=@Revision AND StatusKey='proposed' AND ExpiresAtUtc>@Now
         AND EXISTS (SELECT 1 FROM fn_workflow_definition_version AS version
         INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId
         INNER JOIN fn_workflow_form_version AS formVersion ON formVersion.Id=version.FormVersionId
         INNER JOIN fn_workflow_form_definition AS formDefinition ON formDefinition.Id=formVersion.FormDefinitionId
         WHERE version.Id=@TargetDefinitionVersionId AND definition.DefinitionKey=@TargetWorkflowKey
         AND definition.TenantScopeKey=@TenantScopeKey AND definition.StatusKey='active'
         AND formDefinition.TenantScopeKey=@TenantScopeKey AND formDefinition.StatusKey='active')
        """,SqlDataScope.Global);
    public static readonly SqlStatement Revoke=new("workflow.collaboration_grant.revoke","""
        UPDATE fn_workflow_collaboration_grant SET StatusKey='revoked',Revision=Revision+1,
         RevokedAtUtc=@Now,RevokedById=@ActorUserId,RevokedByScopeKey=@TenantScopeKey,RevocationReason=@Reason
         WHERE Id=@Id AND (SourceTenantScopeKey=@TenantScopeKey OR TargetTenantScopeKey=@TenantScopeKey)
         AND Revision=@Revision AND StatusKey IN ('proposed','active')
        """,SqlDataScope.Global);
    public static readonly SqlStatement InsertAction=new("workflow.collaboration_grant.insert_action","""
        INSERT INTO fn_workflow_collaboration_grant_action (Id,GrantId,ActorTenantScopeKey,ActorUserId,OperationKey,
         IdempotencyKey,RequestHash,ResponseJson,ResultRevision,ResultStatusKey,CreatedAtUtc)
         SELECT @ActionId,grantRow.Id,@TenantScopeKey,@ActorUserId,@OperationKey,@IdempotencyKey,@RequestHash,@ResponseJson,
         grantRow.Revision,grantRow.StatusKey,@Now FROM fn_workflow_collaboration_grant AS grantRow
         WHERE grantRow.Id=@Id AND (grantRow.SourceTenantScopeKey=@TenantScopeKey OR grantRow.TargetTenantScopeKey=@TenantScopeKey)
         AND grantRow.Revision=@Revision AND grantRow.StatusKey=@StatusKey
        """,SqlDataScope.Global);
}

/// <summary>保存第一次响应，后续撤销也不能改写原幂等回执。</summary>
internal sealed record WorkflowCollaborationGrantActionRecord(string RequestHash,string ResponseJson);
