namespace Full.NET.Modules.Identity.Domain;

internal sealed record RefreshSession(
    Guid Id,
    Guid UserId,
    Guid FamilyId,
    string ClientId,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    Guid? ReplacedById,
    Guid? ActiveTenantId,
    DateTimeOffset CreatedAtUtc,
    int Version);
