namespace Full.NET.Modules.Identity.Features.ManageTenantMembers;

internal sealed record TenantMemberRecord(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string MemberRole,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

internal sealed record TenantMemberListRow(
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

internal sealed record TenantInvitationRecord(
    Guid Id,
    Guid TenantId,
    string TargetEmail,
    Guid? TargetUserId,
    Guid InvitedByUserId,
    string MemberRole,
    string TokenHash,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);
