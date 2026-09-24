namespace Full.NET.Modules.Identity.Persistence;

internal sealed record RegistrationInvitationRecord(
    Guid InvitationId,
    Guid TenantId,
    string NormalizedEmail,
    string CredentialHash,
    Guid RegistrationWayId,
    byte Status,
    Guid? BoundUserId,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset CreatedAtUtc,
    int Version)
{
    // MySQL 驱动返回 sbyte 和 DateTime；Dapper 使用无参构造后逐列转换，避免位置构造签名不匹配。
    public RegistrationInvitationRecord()
        : this(
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            string.Empty,
            Guid.Empty,
            0,
            null,
            DateTimeOffset.MinValue,
            null,
            null,
            DateTimeOffset.MinValue,
            0)
    {
    }
}
