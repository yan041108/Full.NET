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
    int Version);
