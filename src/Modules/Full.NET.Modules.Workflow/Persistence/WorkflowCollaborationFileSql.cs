using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>准备阶段不写共享准入或Outbox；只有冻结成功后目标才能看见能力投影。</summary>
internal static class WorkflowCollaborationFileSql
{
    public static readonly SqlStatement ReadVisibleNames = new("workflow.collaboration_file.visible_names", """
        SELECT DISTINCT remoteFile.CapabilityId AS FileId,remoteFile.OriginalFileName
        FROM fn_workflow_file_read_reference AS remoteFile
        INNER JOIN fn_workflow_form_submission_attachment AS attachment ON attachment.Id=remoteFile.Id
            AND attachment.InstanceId=remoteFile.InstanceId AND attachment.TenantScopeKey=@TenantScopeKey
            AND attachment.FileId=remoteFile.CapabilityId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=remoteFile.InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        WHERE remoteFile.InstanceId=@InstanceId AND remoteFile.TenantScopeKey=@TenantScopeKey AND remoteFile.CapabilityId IN @FileIds
        """, SqlDataScope.Global);
    private const string Candidate = """
        callState.Id,callState.TenantScopeKey AS SourceTenantScopeKey,grantRow.TargetTenantScopeKey,callState.UpdatedAtUtc
        FROM fn_workflow_collaboration_call AS callState
        INNER JOIN fn_workflow_collaboration_grant AS grantRow ON grantRow.Id=callState.GrantId AND grantRow.SourceTenantScopeKey=callState.TenantScopeKey
        WHERE callState.StatusKey='preparing' AND (@HasAfter=0 OR callState.UpdatedAtUtc>@AfterUpdatedAtUtc OR (callState.UpdatedAtUtc=@AfterUpdatedAtUtc AND callState.Id>@AfterId))
        """;
    public static readonly SqlStatement PendingSqlServer = new("workflow.collaboration_file.pending_sqlserver", "SELECT TOP (@Take) " + Candidate + " ORDER BY callState.UpdatedAtUtc,callState.Id", SqlDataScope.Global);
    public static readonly SqlStatement PendingMySql = new("workflow.collaboration_file.pending_mysql", "SELECT " + Candidate + " ORDER BY callState.UpdatedAtUtc,callState.Id LIMIT @Take", SqlDataScope.Global);
    private const string Cleanup = """
        callState.Id,callState.TenantScopeKey AS SourceTenantScopeKey,grantRow.TargetTenantScopeKey,callState.UpdatedAtUtc
        FROM fn_workflow_collaboration_call AS callState
        INNER JOIN fn_workflow_collaboration_grant AS grantRow ON grantRow.Id=callState.GrantId AND grantRow.SourceTenantScopeKey=callState.TenantScopeKey
        WHERE (callState.StatusKey IN ('denied','cancelled') OR grantRow.StatusKey='revoked' OR grantRow.ExpiresAtUtc<=@Now)
            AND (@HasAfter=0 OR callState.UpdatedAtUtc>@AfterUpdatedAtUtc OR (callState.UpdatedAtUtc=@AfterUpdatedAtUtc AND callState.Id>@AfterId))
        """;
    public static readonly SqlStatement CleanupSqlServer = new("workflow.collaboration_file.cleanup_sqlserver", "SELECT TOP (@Take) " + Cleanup + " ORDER BY callState.UpdatedAtUtc,callState.Id", SqlDataScope.Global);
    public static readonly SqlStatement CleanupMySql = new("workflow.collaboration_file.cleanup_mysql", "SELECT " + Cleanup + " ORDER BY callState.UpdatedAtUtc,callState.Id LIMIT @Take", SqlDataScope.Global);
    public static readonly SqlStatement Freeze = new("workflow.collaboration_file.freeze", """
        UPDATE fn_workflow_collaboration_call SET BindingJson=@BindingJson,BindingHash=@BindingHash,ProjectionJson=@ProjectionJson,ProjectionHash=@ProjectionHash,
            StatusKey='requested',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='preparing' AND Revision=@Revision AND BindingHash=@OldBindingHash
            AND NOT EXISTS (SELECT 1 FROM fn_workflow_collaboration_admission AS admission WHERE admission.Id=fn_workflow_collaboration_call.Id)
        """, SqlDataScope.Global);
    public static readonly SqlStatement ClosePreparing = new("workflow.collaboration_file.close_preparing", """
        UPDATE fn_workflow_collaboration_call SET StatusKey=@StatusKey,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='preparing' AND Revision=@Revision AND BindingHash=@BindingHash
            AND NOT EXISTS (SELECT 1 FROM fn_workflow_collaboration_admission AS admission WHERE admission.Id=fn_workflow_collaboration_call.Id)
        """, SqlDataScope.Global);
    public static readonly SqlStatement IssuingReference = new("workflow.collaboration_file.issuing_reference", """
        SELECT attachment.Id,attachment.FieldKey,
            CASE WHEN @TenantScopeKey='host' THEN attachment.ClaimReferenceId ELSE attachment.OwnerResourceId END AS OwnerReferenceId
        FROM fn_workflow_form_submission_attachment AS attachment
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=attachment.InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        WHERE attachment.InstanceId=@InstanceId AND attachment.TenantScopeKey=@TenantScopeKey AND attachment.FieldKey=@FieldKey AND attachment.FileId=@FileId
        """, SqlDataScope.Global);
    private const string RemoteColumns = """
        remoteFile.InstanceId,remoteFile.CallId,remoteFile.TenantScopeKey,remoteFile.CapabilityId,remoteFile.DirectionKey,remoteFile.CapabilityFieldKey,
        remoteFile.OriginalFileName,remoteFile.ContentType,remoteFile.SizeBytes,remoteFile.ContentHash,remoteFile.ExpiresAtUtc
        """;
    public static readonly SqlStatement FindRemote = new("workflow.collaboration_file.find_remote", $"""
        SELECT DISTINCT {RemoteColumns} FROM fn_workflow_file_read_reference AS remoteFile
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=remoteFile.InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        WHERE remoteFile.InstanceId=@InstanceId AND remoteFile.TenantScopeKey=@TenantScopeKey AND remoteFile.CapabilityId=@FileId
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindMappedRemote = new("workflow.collaboration_file.find_mapped_remote", $"""
        SELECT DISTINCT {RemoteColumns} FROM fn_workflow_file_read_reference AS remoteFile
        INNER JOIN fn_workflow_form_submission_attachment AS attachment ON attachment.Id=remoteFile.Id AND attachment.InstanceId=remoteFile.InstanceId AND attachment.TenantScopeKey=@TenantScopeKey
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=remoteFile.InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        WHERE remoteFile.InstanceId=@InstanceId AND remoteFile.TenantScopeKey=@TenantScopeKey AND remoteFile.CapabilityId=@FileId AND attachment.FieldKey=@FieldKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertRemote = new("workflow.collaboration_file.insert_remote", """
        INSERT INTO fn_workflow_file_read_reference
            (Id,InstanceId,CallId,TenantScopeKey,CapabilityId,DirectionKey,CapabilityFieldKey,OriginalFileName,ContentType,SizeBytes,ContentHash,ExpiresAtUtc)
        SELECT @Id,instance.Id,@CallId,instance.TenantScopeKey,@FileId,@DirectionKey,@CapabilityFieldKey,@OriginalFileName,@ContentType,@SizeBytes,@ContentHash,@ExpiresAtUtc
        FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindParent = new("workflow.collaboration_file.find_parent", """
        SELECT item.InstanceId FROM fn_workflow_subprocess_item AS item
        INNER JOIN fn_workflow_subprocess_call AS callState ON callState.Id=item.CallId AND callState.InstanceId=item.InstanceId AND callState.TenantScopeKey=@TenantScopeKey
        INNER JOIN fn_workflow_instance AS child ON child.Id=item.ChildInstanceId AND child.TenantScopeKey=@TenantScopeKey
            AND child.DefinitionVersionId=callState.ChildDefinitionVersionId AND child.FormVersionId=callState.ChildFormVersionId
        WHERE item.ChildInstanceId=@InstanceId AND item.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
}
