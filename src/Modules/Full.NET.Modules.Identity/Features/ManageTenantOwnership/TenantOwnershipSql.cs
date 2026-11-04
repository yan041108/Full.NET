using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Features.ManageTenantOwnership;

/// <summary>所有者职责与回执只写 Identity 自有表；最多读取两名 Owner 以拒绝歧义，不猜测历史目录字段。</summary>
internal static class TenantOwnershipSql
{
    internal static readonly SqlStatement OwnersSqlServer = new("identity.tenant_ownership.owners_sqlserver", """
        SELECT TOP (2) Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version
        FROM fn_identity_tenant_member
        WHERE TenantId=@TenantId AND MemberRole='Owner'
        ORDER BY Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement OwnersMySql = new("identity.tenant_ownership.owners_mysql", """
        SELECT Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version
        FROM fn_identity_tenant_member
        WHERE TenantId=@TenantId AND MemberRole='Owner'
        ORDER BY Id LIMIT 2
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindReceipt = new("identity.tenant_ownership.find_receipt", """
        SELECT TenantId,OperationId,ActorUserId,SourceMemberId,SourceUserId,SourcePreviousVersion,SourceVersion,
               TargetMemberId,TargetUserId,TargetPreviousVersion,TargetVersion,CreatedAtUtc,RequestDigest
        FROM fn_identity_tenant_owner_transfer
        WHERE TenantId=@TenantId AND OperationId=@OperationId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertReceipt = new("identity.tenant_ownership.insert_receipt", """
        INSERT INTO fn_identity_tenant_owner_transfer
            (Id,TenantId,OperationId,ActorUserId,SourceMemberId,SourceUserId,SourcePreviousVersion,SourceVersion,
             TargetMemberId,TargetUserId,TargetPreviousVersion,TargetVersion,CreatedAtUtc,RequestDigest)
        VALUES (@Id,@TenantId,@OperationId,@ActorUserId,@SourceMemberId,@SourceUserId,@SourcePreviousVersion,@SourceVersion,
                @TargetMemberId,@TargetUserId,@TargetPreviousVersion,@TargetVersion,@CreatedAtUtc,@RequestDigest)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    // 账号停用与职责交接按同一 Host 用户行串行化；仅锁定本次两个显式参与者，调用方统一排序。
    internal static readonly SqlStatement LockHostUserSqlServer = new("identity.tenant_ownership.lock_host_user_sqlserver", """
        SELECT Id FROM fn_identity_user WITH (UPDLOCK,ROWLOCK)
        WHERE Id=@UserId AND TenantId IS NULL AND ScopeKey='host'
        """, SqlDataScope.Global);
    internal static readonly SqlStatement LockHostUserMySql = new("identity.tenant_ownership.lock_host_user_mysql", """
        SELECT Id FROM fn_identity_user
        WHERE Id=@UserId AND TenantId IS NULL AND ScopeKey='host' FOR UPDATE
        """, SqlDataScope.Global);
    internal static readonly SqlStatement HasOwnerResponsibility = new("identity.tenant_ownership.has_owner_responsibility", """
        SELECT 1 FROM fn_identity_user AS actor
        WHERE actor.Id=@UserId AND actor.TenantId IS NULL AND actor.ScopeKey='host'
          AND EXISTS (SELECT 1 FROM fn_identity_tenant_member AS member
                      WHERE member.UserId=actor.Id AND member.MemberRole='Owner')
        """, SqlDataScope.HostOnly);
}
