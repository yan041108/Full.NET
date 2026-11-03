using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>能力只由本地持有派生；跨作用域读取限定精确接收方和调用，不改变当前租户。</summary>
internal static class WorkflowFileReadCapabilitySql
{
    private const string Columns = """
        cap.Id,cap.CallId,cap.IssuerTenantId,cap.IssuerScopeKey,cap.RecipientTenantId,cap.RecipientScopeKey,cap.DirectionKey,cap.FieldKey,
        cap.SourceFileId,cap.OwnerReferenceId,cap.OriginalFileName,cap.ContentType,cap.SizeBytes,cap.ContentHash,cap.StatusKey,
        cap.ExpiresAtUtc,cap.CreatedAtUtc,cap.RevokedAtUtc,cap.Revision
        """;
    private const string ReadyJoin = """
        FROM fn_files_read_capability AS cap
        LEFT JOIN fn_files_file AS hostFile ON cap.IssuerTenantId IS NULL AND hostFile.Id=cap.SourceFileId AND hostFile.TenantId IS NULL
        LEFT JOIN fn_files_tenant_resource_file AS tenantFile ON cap.IssuerTenantId IS NOT NULL AND tenantFile.Id=cap.SourceFileId AND tenantFile.TenantId=cap.IssuerTenantId
        """;
    private const string ReadyProof = """
        AND ((cap.IssuerTenantId IS NULL AND hostFile.StorageState='ready' AND hostFile.DeletedAtUtc IS NULL
            AND LOWER(hostFile.ContentHash)=cap.ContentHash AND hostFile.SizeBytes=cap.SizeBytes
            AND EXISTS (SELECT 1 FROM fn_files_file_reference_claim AS claim
                WHERE claim.FileId=cap.SourceFileId AND claim.ConsumerModule='workflow' AND claim.ConsumerReferenceId=cap.OwnerReferenceId
                    AND claim.State='active' AND LOWER(claim.ContentHash)=cap.ContentHash AND claim.SizeBytes=cap.SizeBytes))
            OR (cap.IssuerTenantId IS NOT NULL AND tenantFile.OwnerModuleKey='workflow' AND tenantFile.ResourceId=cap.OwnerReferenceId
                AND tenantFile.StatusKey='ready' AND LOWER(tenantFile.ContentHash)=cap.ContentHash AND tenantFile.SizeBytes=cap.SizeBytes))
        """;
    private const string ReadyColumns = Columns + """
        ,CASE WHEN cap.IssuerTenantId IS NULL THEN hostFile.ProviderKey ELSE tenantFile.ProviderKey END AS ProviderKey,
        CASE WHEN cap.IssuerTenantId IS NULL THEN hostFile.StorageKey ELSE tenantFile.StorageKey END AS StorageKey
        """;
    private const string ReadyColumnsSqlServer = Columns + """
        ,CASE WHEN cap.IssuerTenantId IS NULL THEN hostFile.ProviderKey COLLATE Latin1_General_100_BIN2 ELSE tenantFile.ProviderKey COLLATE Latin1_General_100_BIN2 END AS ProviderKey,
        CASE WHEN cap.IssuerTenantId IS NULL THEN hostFile.StorageKey COLLATE Latin1_General_100_BIN2 ELSE tenantFile.StorageKey COLLATE Latin1_General_100_BIN2 END AS StorageKey
        """;
    // SQL Server历史表使用数据库默认排序规则，摘要比较显式使用二进制规则，不改变任何历史表。
    private const string ReadyProofSqlServer = """
        AND ((cap.IssuerTenantId IS NULL AND hostFile.StorageState='ready' AND hostFile.DeletedAtUtc IS NULL
            AND LOWER(hostFile.ContentHash)=cap.ContentHash COLLATE Latin1_General_100_BIN2 AND hostFile.SizeBytes=cap.SizeBytes
            AND EXISTS (SELECT 1 FROM fn_files_file_reference_claim AS claim
                WHERE claim.FileId=cap.SourceFileId AND claim.ConsumerModule='workflow' AND claim.ConsumerReferenceId=cap.OwnerReferenceId
                    AND claim.State='active' AND LOWER(claim.ContentHash)=cap.ContentHash COLLATE Latin1_General_100_BIN2 AND claim.SizeBytes=cap.SizeBytes))
            OR (cap.IssuerTenantId IS NOT NULL AND tenantFile.OwnerModuleKey='workflow' AND tenantFile.ResourceId=cap.OwnerReferenceId
                AND tenantFile.StatusKey='ready' AND LOWER(tenantFile.ContentHash)=cap.ContentHash COLLATE Latin1_General_100_BIN2 AND tenantFile.SizeBytes=cap.SizeBytes))
        """;
    public static readonly SqlStatement FindIssued = new("files.read_capability.find_issued", $"""
        SELECT {Columns},CAST(NULL AS CHAR(64)) AS ProviderKey,CAST(NULL AS CHAR(512)) AS StorageKey
        FROM fn_files_read_capability AS cap
        WHERE cap.CallId=@CallId AND cap.IssuerScopeKey=@TenantScopeKey AND cap.DirectionKey=@DirectionKey AND cap.FieldKey=@FieldKey AND cap.SourceFileId=@FileId
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindReceivedReady = new("files.read_capability.find_received_ready", $"""
        SELECT {ReadyColumns} {ReadyJoin}
        WHERE cap.Id=@Id AND cap.CallId=@CallId AND cap.RecipientScopeKey=@TenantScopeKey AND cap.StatusKey='active' AND cap.ExpiresAtUtc>@Now
        {ReadyProof}
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindOwnReady = new("files.read_capability.find_own_ready", $"""
        SELECT {ReadyColumns} {ReadyJoin}
        WHERE cap.Id=@Id AND cap.CallId=@CallId AND cap.IssuerScopeKey=@TenantScopeKey AND cap.StatusKey='active' AND cap.ExpiresAtUtc>@Now
        {ReadyProof}
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindReceivedReadySqlServer = new("files.read_capability.find_received_ready_sqlserver", $"""
        SELECT {ReadyColumnsSqlServer} {ReadyJoin}
        WHERE cap.Id=@Id AND cap.CallId=@CallId AND cap.RecipientScopeKey=@TenantScopeKey AND cap.StatusKey='active' AND cap.ExpiresAtUtc>@Now
        {ReadyProofSqlServer}
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindOwnReadySqlServer = new("files.read_capability.find_own_ready_sqlserver", $"""
        SELECT {ReadyColumnsSqlServer} {ReadyJoin}
        WHERE cap.Id=@Id AND cap.CallId=@CallId AND cap.IssuerScopeKey=@TenantScopeKey AND cap.StatusKey='active' AND cap.ExpiresAtUtc>@Now
        {ReadyProofSqlServer}
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertHost = new("files.read_capability.insert_host", """
        INSERT INTO fn_files_read_capability
            (Id,CallId,IssuerTenantId,IssuerScopeKey,RecipientTenantId,RecipientScopeKey,DirectionKey,FieldKey,SourceFileId,OwnerReferenceId,
             OriginalFileName,ContentType,SizeBytes,ContentHash,StatusKey,ExpiresAtUtc,CreatedAtUtc,RevokedAtUtc,Revision)
        SELECT @Id,@CallId,NULL,'host',@RecipientTenantId,@RecipientScopeKey,@DirectionKey,@FieldKey,fileState.Id,@OwnerReferenceId,
            fileState.OriginalFileName,fileState.ContentType,fileState.SizeBytes,LOWER(fileState.ContentHash),'active',@ExpiresAtUtc,@Now,NULL,1
        FROM fn_files_file AS fileState WHERE fileState.Id=@FileId AND fileState.TenantId IS NULL
            AND fileState.StorageState='ready' AND fileState.DeletedAtUtc IS NULL AND fileState.SizeBytes>0 AND fileState.ContentHash IS NOT NULL
            AND EXISTS (SELECT 1 FROM fn_files_file_reference_claim AS claim WHERE claim.FileId=fileState.Id AND claim.ConsumerModule='workflow'
                AND claim.ConsumerReferenceId=@OwnerReferenceId AND claim.State='active' AND LOWER(claim.ContentHash)=LOWER(fileState.ContentHash) AND claim.SizeBytes=fileState.SizeBytes)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertTenant = new("files.read_capability.insert_tenant", """
        INSERT INTO fn_files_read_capability
            (Id,CallId,IssuerTenantId,IssuerScopeKey,RecipientTenantId,RecipientScopeKey,DirectionKey,FieldKey,SourceFileId,OwnerReferenceId,
             OriginalFileName,ContentType,SizeBytes,ContentHash,StatusKey,ExpiresAtUtc,CreatedAtUtc,RevokedAtUtc,Revision)
        SELECT @Id,@CallId,@TenantId,@TenantScopeKey,@RecipientTenantId,@RecipientScopeKey,@DirectionKey,@FieldKey,fileState.Id,@OwnerReferenceId,
            fileState.OriginalFileName,fileState.ContentType,fileState.SizeBytes,LOWER(fileState.ContentHash),'active',@ExpiresAtUtc,@Now,NULL,1
        FROM fn_files_tenant_resource_file AS fileState WHERE fileState.Id=@FileId AND fileState.TenantId=@TenantId
            AND fileState.OwnerModuleKey='workflow' AND fileState.ResourceId=@OwnerReferenceId AND fileState.StatusKey='ready' AND fileState.SizeBytes>0
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListToReleaseSqlServer = new("files.read_capability.list_to_release_sqlserver", """
        SELECT TOP (25) Id FROM fn_files_read_capability WHERE CallId=@CallId AND IssuerScopeKey=@TenantScopeKey AND StatusKey='active' ORDER BY Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListToReleaseMySql = new("files.read_capability.list_to_release_mysql", """
        SELECT Id FROM fn_files_read_capability WHERE CallId=@CallId AND IssuerScopeKey=@TenantScopeKey AND StatusKey='active' ORDER BY Id LIMIT 25
        """, SqlDataScope.Global);
    public static readonly SqlStatement RevokeIssued = new("files.read_capability.revoke_issued", """
        UPDATE fn_files_read_capability SET StatusKey='revoked',RevokedAtUtc=@Now,Revision=Revision+1
        WHERE Id IN @Ids AND CallId=@CallId AND IssuerScopeKey=@TenantScopeKey AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListExpiredSqlServer = new("files.read_capability.list_expired_sqlserver", """
        SELECT TOP (25) Id FROM fn_files_read_capability WHERE StatusKey='active' AND ExpiresAtUtc<=@Now ORDER BY ExpiresAtUtc,Id
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListExpiredMySql = new("files.read_capability.list_expired_mysql", """
        SELECT Id FROM fn_files_read_capability WHERE StatusKey='active' AND ExpiresAtUtc<=@Now ORDER BY ExpiresAtUtc,Id LIMIT 25
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement RevokeExpired = new("files.read_capability.revoke_expired", """
        UPDATE fn_files_read_capability SET StatusKey='revoked',RevokedAtUtc=@Now,Revision=Revision+1
        WHERE Id IN @Ids AND StatusKey='active' AND ExpiresAtUtc<=@Now
        """, SqlDataScope.HostOnly);
}
