using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>先准备当前查询的有界授权事实，COUNT和分页再次核对当前grant/准入，不能在分页后删除无权行。</summary>
internal static class WorkflowCollaborationListSql
{
    private const string CandidateColumns = """
        admission.Id,admission.SourceTenantScopeKey,admission.TargetTenantScopeKey,admission.DeliveryId,admission.GrantId,
        admission.BindingJson,admission.BindingHash,admission.ProjectionHash,admission.StatusKey,
        admission.TargetInstanceId,admission.Revision
        """;
    private const string Candidates = """
        FROM fn_workflow_collaboration_admission AS admission
        INNER JOIN fn_workflow_collaboration_grant AS grantRow ON grantRow.Id=admission.GrantId
            AND grantRow.SourceTenantScopeKey=admission.SourceTenantScopeKey AND grantRow.TargetTenantScopeKey=@TenantScopeKey
        WHERE admission.TargetTenantScopeKey=@TenantScopeKey AND admission.StatusKey IN ('created','active','completed')
            AND grantRow.StatusKey='active' AND grantRow.ExpiresAtUtc>@Now
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_origin AS origin
                INNER JOIN fn_workflow_instance AS instance ON instance.Id=origin.Id AND instance.TenantScopeKey=@TenantScopeKey
                INNER JOIN fn_workflow_definition_version AS version ON version.Id=instance.DefinitionVersionId
                INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId
                WHERE origin.CallId=admission.Id AND origin.RootInstanceId=admission.TargetInstanceId AND origin.TenantScopeKey=@TenantScopeKey
                    AND (@DefinitionKey IS NULL OR definition.DefinitionKey=@DefinitionKey)
                    AND (@DefinitionVersionId IS NULL OR instance.DefinitionVersionId=@DefinitionVersionId)
                    AND (@BusinessType IS NULL OR instance.BusinessType=@BusinessType)
                    AND ((@QueryKind='instance' AND (@StartedById IS NULL OR instance.StartedById=@StartedById)
                            AND (@StatusKey IS NULL OR instance.StatusKey=@StatusKey)
                            AND (@StartedFromUtc IS NULL OR instance.StartedAtUtc>=@StartedFromUtc)
                            AND (@StartedToUtc IS NULL OR instance.StartedAtUtc<=@StartedToUtc))
                        OR (@QueryKind IN ('pending','history') AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo
                            WHERE todo.InstanceId=instance.Id AND todo.AssigneeUserId=@ActorUserId
                                AND (@ResultActionKey IS NULL OR todo.ResultActionKey=@ResultActionKey)
                                AND ((@QueryKind='pending' AND todo.StatusKey IN ('active','before_countersign') AND instance.StatusKey='active'
                                        AND (@ArrivedFromUtc IS NULL OR todo.ArrivedAtUtc>=@ArrivedFromUtc)
                                        AND (@ArrivedToUtc IS NULL OR todo.ArrivedAtUtc<=@ArrivedToUtc))
                                    OR (@QueryKind='history' AND todo.StatusKey IN ('completed','cancelled') AND todo.CompletedAtUtc IS NOT NULL
                                        AND (@CompletedFromUtc IS NULL OR todo.CompletedAtUtc>=@CompletedFromUtc)
                                        AND (@CompletedToUtc IS NULL OR todo.CompletedAtUtc<=@CompletedToUtc)))))
                        OR (@QueryKind='claim' AND instance.StatusKey='active' AND EXISTS (SELECT 1 FROM fn_workflow_claim AS pool
                            INNER JOIN fn_workflow_claim_candidate AS candidate ON candidate.PoolId=pool.Id AND candidate.InstanceId=pool.InstanceId
                            WHERE pool.InstanceId=instance.Id AND pool.StatusKey='available' AND candidate.UserId=@ActorUserId))
                        OR (@QueryKind='recovery' AND EXISTS (SELECT 1 FROM fn_workflow_recovery_task AS task
                            WHERE task.InstanceId=instance.Id AND task.TenantScopeKey=@TenantScopeKey))))
        """;
    public static readonly SqlStatement CandidatesSqlServer = new("workflow.collaboration_query.candidates_sqlserver", "SELECT TOP (@CollaborationTake) " + CandidateColumns + "\n" + Candidates + "\n" + """
        AND EXISTS (SELECT 1 FROM OPENJSON(grantRow.AcceptanceJson,'$.allowedUserIds') AS member WHERE member.value=@ActorUserText)
        ORDER BY admission.Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement CandidatesMySql = new("workflow.collaboration_query.candidates_mysql", "SELECT " + CandidateColumns + "\n" + Candidates + "\n" + """
        AND JSON_CONTAINS(grantRow.AcceptanceJson,JSON_QUOTE(@ActorUserText),'$.allowedUserIds')=1
        ORDER BY admission.Id LIMIT @CollaborationTake
        """, SqlDataScope.Global);

    public static readonly SqlStatement Grants = new("workflow.collaboration_query.grants", """
        SELECT grantRow.Id,grantRow.SourceTenantId,grantRow.TargetTenantId,grantRow.SourceTenantScopeKey,grantRow.TargetTenantScopeKey,
            grantRow.Revision,grantRow.StatusKey,grantRow.SourceActorId,grantRow.TargetActorId,grantRow.OfferJson,grantRow.AcceptanceJson,
            grantRow.PolicyHash,grantRow.ExpiresAtUtc,grantRow.CreatedAtUtc,grantRow.AcceptedAtUtc,grantRow.RevokedAtUtc,grantRow.RevokedById,
            grantRow.RevokedByScopeKey,grantRow.SourceDefinitionId,grantRow.SourceFormVersionId,grantRow.TargetDefinitionVersionId,
            grantRow.TargetSummaryJson,grantRow.TargetSummaryHash
        FROM fn_workflow_collaboration_grant AS grantRow WHERE grantRow.Id IN @GrantIds AND grantRow.TargetTenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    private const string NoOrigin = """
        NOT EXISTS (SELECT 1 FROM fn_workflow_collaboration_origin AS localOrigin
            WHERE localOrigin.Id=instance.Id AND localOrigin.TenantScopeKey=@TenantScopeKey)
        """;
    private const string ReadableOrigin = """
        FROM fn_workflow_collaboration_origin AS localOrigin
        INNER JOIN fn_workflow_collaboration_admission AS admission ON admission.Id=localOrigin.CallId AND admission.TargetInstanceId=localOrigin.RootInstanceId
        INNER JOIN fn_workflow_instance AS originRoot ON originRoot.Id=localOrigin.RootInstanceId AND originRoot.TenantScopeKey=@TenantScopeKey
        INNER JOIN fn_workflow_collaboration_grant AS grantRow ON grantRow.Id=admission.GrantId AND grantRow.TargetTenantScopeKey=@TenantScopeKey
        """;
    private const string ReadableWhere = """
        WHERE localOrigin.Id=instance.Id AND localOrigin.TenantScopeKey=@TenantScopeKey AND admission.TargetTenantScopeKey=@TenantScopeKey
            AND grantRow.SourceTenantScopeKey=admission.SourceTenantScopeKey AND grantRow.StatusKey='active' AND grantRow.ExpiresAtUtc>@Now
            AND (admission.StatusKey IN ('created','active') OR (admission.StatusKey='completed' AND originRoot.StatusKey='completed'))
        """;
    internal const string AllowsSqlServer = "(" + NoOrigin + " OR EXISTS (SELECT 1 " + ReadableOrigin + "\n" + """
        INNER JOIN OPENJSON(@CollaborationProofs) WITH (Id uniqueidentifier '$.id',BindingHash nvarchar(64) '$.bindingHash',
            GrantRevision bigint '$.grantRevision',PolicyHash nvarchar(64) '$.policyHash') AS proof ON proof.Id=admission.Id
        """ + "\n" + ReadableWhere + "\n" + """
            AND proof.BindingHash COLLATE Latin1_General_100_BIN2=admission.BindingHash
            AND proof.GrantRevision=grantRow.Revision AND proof.PolicyHash COLLATE Latin1_General_100_BIN2=grantRow.PolicyHash))
        """;
    internal const string AllowsMySql = "(" + NoOrigin + " OR EXISTS (SELECT 1 " + ReadableOrigin + "\n" + """
        INNER JOIN JSON_TABLE(@CollaborationProofs,'$[*]' COLUMNS (Id char(36) PATH '$.id',BindingHash varchar(64) PATH '$.bindingHash',
            GrantRevision bigint PATH '$.grantRevision',PolicyHash varchar(64) PATH '$.policyHash')) AS proof ON UNHEX(REPLACE(proof.Id,'-',''))=admission.Id
        """ + "\n" + ReadableWhere + "\n" + """
            AND proof.BindingHash=admission.BindingHash AND proof.GrantRevision=grantRow.Revision AND proof.PolicyHash=grantRow.PolicyHash))
        """;
}
