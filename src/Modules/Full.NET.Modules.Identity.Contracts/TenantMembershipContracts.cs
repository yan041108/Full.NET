namespace Full.NET.Modules.Identity.Contracts;

public static class IdentityTenantMembershipPermissions
{
    public const string Read = "identity.tenant_members.read";
    public const string Invite = "identity.tenant_members.invite";
    public const string Update = "identity.tenant_members.update";
    public const string Remove = "identity.tenant_members.remove";
    public const string RevokeInvitation = "identity.tenant_members.revoke_invitation";
}

public static class TenantMemberRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";
}

public static class TenantMemberStatuses
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Removed = "Removed";
}

public static class TenantInvitationStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
}

public sealed record TenantMemberResponse(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string Username,
    string DisplayName,
    string MemberRole,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

public sealed record TenantInvitationResponse(
    Guid Id,
    Guid TenantId,
    string TargetEmail,
    Guid? TargetUserId,
    Guid InvitedByUserId,
    string MemberRole,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

public sealed record CreateTenantInvitationRequest(
    string TargetEmail,
    string MemberRole,
    int ExpiresInHours = 72);

public sealed record CreateTenantInvitationResult(
    TenantInvitationResponse Invitation,
    string InvitationToken);

public sealed record UpdateTenantMemberRequest(
    string MemberRole,
    int Version);

public sealed record AcceptTenantInvitationRequest(string InvitationToken);

public sealed record AcceptTenantInvitationResponse(
    Guid MemberId,
    Guid TenantId,
    Guid UserId,
    string MemberRole,
    string Status);
