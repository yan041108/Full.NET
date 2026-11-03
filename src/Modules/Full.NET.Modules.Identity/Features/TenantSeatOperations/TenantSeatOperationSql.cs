using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Features.TenantSeatOperations;

/// <summary>后台只能按本模块持久工作项操作成员；所有写入同时锚定租户、成员及原始版本。</summary>
internal static class TenantSeatOperationSql
{
    internal const string Columns = "Id, RequestKey, TenantId, UserId, MemberId, InvitationId, PreviousRole, PreviousStatus, MemberVersion, InvitationVersion, Phase, RequestHash, ErrorCode, CreatedAtUtc, DueAtUtc, LeaseId, LeaseUntilUtc, Version, Attempt, RegistrationWayId";
    internal static readonly SqlStatement Find = new("identity.seat_operation.find",
        $"SELECT {Columns} FROM fn_identity_member_seat_operation WHERE Id=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindRequest = new("identity.seat_operation.find_request",
        $"SELECT {Columns} FROM fn_identity_member_seat_operation WHERE TenantId=@TenantId AND RequestKey=@RequestKey", SqlDataScope.HostOnly);
    internal static readonly SqlStatement Insert = new("identity.seat_operation.insert", $"""
        INSERT INTO fn_identity_member_seat_operation ({Columns})
        VALUES (@Id,@RequestKey,@TenantId,@UserId,@MemberId,@InvitationId,@PreviousRole,@PreviousStatus,
          @MemberVersion,@InvitationVersion,@Phase,@RequestHash,NULL,@CreatedAtUtc,@DueAtUtc,NULL,NULL,1,1,@RegistrationWayId)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DueSqlServer = new("identity.seat_operation.due.sql_server", """
        SELECT TOP (20) Id FROM fn_identity_member_seat_operation
        WHERE Phase NOT IN ('Activated','Cancelled') AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now)
        ORDER BY DueAtUtc,Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DueMySql = new("identity.seat_operation.due.my_sql", """
        SELECT Id FROM fn_identity_member_seat_operation
        WHERE Phase NOT IN ('Activated','Cancelled') AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now)
        ORDER BY DueAtUtc,Id LIMIT 20
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Acquire = new("identity.seat_operation.acquire", """
        UPDATE fn_identity_member_seat_operation SET LeaseId=@LeaseId,LeaseUntilUtc=@Until,Version=Version+1
        WHERE Id=@Id AND Phase NOT IN ('Activated','Cancelled') AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Retry = new("identity.seat_operation.retry", """
        UPDATE fn_identity_member_seat_operation
        SET Phase='Requested',ErrorCode=NULL,MemberVersion=@MemberVersion,Attempt=Attempt+1,DueAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND Phase='Cancelled' AND Version=@Version AND LeaseId IS NULL
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindInvitation = new("identity.seat_operation.find_invitation", $"""
        SELECT {Columns} FROM fn_identity_member_seat_operation
        WHERE TenantId=@TenantId AND InvitationId=@InvitationId AND UserId=@UserId AND InvitationVersion=@InvitationVersion
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Save = new("identity.seat_operation.save", """
        UPDATE fn_identity_member_seat_operation SET Phase=@Phase,ErrorCode=@ErrorCode,DueAtUtc=@DueAtUtc,Version=Version+1
        WHERE Id=@Id AND LeaseId=@LeaseId AND Phase=@ExpectedPhase
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReleaseLease = new("identity.seat_operation.release_lease", """
        UPDATE fn_identity_member_seat_operation SET LeaseId=NULL,LeaseUntilUtc=NULL,DueAtUtc=@DueAtUtc,Version=Version+1
        WHERE Id=@Id AND LeaseId=@LeaseId
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ActivateMember = new("identity.seat_operation.activate_member", """
        UPDATE fn_identity_tenant_member SET Status='Active',UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@MemberId AND TenantId=@TenantId AND UserId=@UserId AND Status='Pending' AND Version=@MemberVersion
          AND EXISTS (SELECT 1 FROM fn_identity_user WHERE Id=@UserId AND IsActive=1)
          AND (@InvitationId IS NULL OR EXISTS
            (SELECT 1 FROM fn_identity_tenant_invitation WHERE Id=@InvitationId AND TenantId=@TenantId
             AND TargetUserId=@UserId AND Status='Accepted' AND Version=@InvitationVersion))
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement RestoreMember = new("identity.seat_operation.restore_member", """
        UPDATE fn_identity_tenant_member SET Status=@PreviousStatus,MemberRole=@PreviousRole,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@MemberId AND TenantId=@TenantId AND UserId=@UserId AND Status='Pending' AND Version=@MemberVersion
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement RestoreInvitation = new("identity.seat_operation.restore_invitation", """
        UPDATE fn_identity_tenant_invitation SET Status='Pending',UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@InvitationId AND TenantId=@TenantId AND Status='Accepted' AND Version=@InvitationVersion
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CancelMember = new("identity.seat_operation.cancel_member", """
        UPDATE fn_identity_member_seat_operation SET Phase='Cancelling',ErrorCode='identity.seat_operation.member_removed',DueAtUtc=@Now,Version=Version+1
        WHERE TenantId=@TenantId AND MemberId=@MemberId AND Phase NOT IN ('Cancelling','Cancelled')
        """, SqlDataScope.HostOnly);
}
