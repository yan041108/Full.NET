using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;

namespace Full.NET.Modules.Identity.Features.MyTenantRegistrations;

/// <summary>本人入驻由可信用户标识限定，目标企业及方式只能从原席位意图读取。</summary>
internal static class SelfTenantRegistrationSql
{
    private const string Projection = """
        operation.Id,operation.TenantId,way.Name AS RegistrationWayName,operation.Phase,operation.ErrorCode,
        operation.CreatedAtUtc,operation.Version,
        CASE WHEN operation.Phase<>'Activated' AND way.IsEnabled=1
          AND (operation.ErrorCode IS NULL OR operation.ErrorCode NOT IN ('identity.seat_operation.member_removed','identity.seat_operation.member_changed'))
          THEN 1 ELSE 0 END AS CanContinue
        """;
    private const string Source = """
        FROM fn_identity_member_seat_operation AS operation
        LEFT JOIN fn_identity_user_registration_way AS way ON way.Id=operation.RegistrationWayId AND way.TenantId=operation.TenantId
        WHERE operation.UserId=@UserId AND operation.RegistrationWayId IS NOT NULL AND operation.InvitationId IS NULL
        """;
    internal static readonly SqlStatement ListSqlServer = new("identity.self_registration.list.sql_server",
        $"SELECT TOP (20) {Projection} {Source} ORDER BY operation.CreatedAtUtc DESC,operation.Id DESC", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySql = new("identity.self_registration.list.my_sql",
        $"SELECT {Projection} {Source} ORDER BY operation.CreatedAtUtc DESC,operation.Id DESC LIMIT 20", SqlDataScope.HostOnly);
    internal static readonly SqlStatement Response = new("identity.self_registration.response",
        $"SELECT {Projection} {Source} AND operation.Id=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement Find = new("identity.self_registration.find", $"""
        SELECT {TenantSeatOperationSql.Columns} FROM fn_identity_member_seat_operation
        WHERE Id=@Id AND UserId=@UserId AND RegistrationWayId IS NOT NULL AND InvitationId IS NULL
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindMember = new("identity.self_registration.find_member", """
        SELECT Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version
        FROM fn_identity_tenant_member WHERE Id=@MemberId AND TenantId=@TenantId AND UserId=@UserId
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement RetryMember = new("identity.self_registration.retry_member", """
        UPDATE fn_identity_tenant_member SET Status='Pending',UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@MemberId AND TenantId=@TenantId AND UserId=@UserId AND Version=@MemberVersion
          AND Status='Removed' AND MemberRole='Member'
        """, SqlDataScope.HostOnly);
}

/// <summary>源行只映射公开进度列，避免内部租约或请求摘要进入序列化。</summary>
internal sealed class SelfTenantRegistrationRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? RegistrationWayName { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int Version { get; set; }
    public bool CanContinue { get; set; }

    internal Contracts.SelfTenantRegistrationResponse ToResponse() => new(Id, TenantId, RegistrationWayName, Phase, ErrorCode, CreatedAtUtc, Version, CanContinue);
}
