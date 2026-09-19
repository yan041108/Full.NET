namespace Full.NET.Modules.Identity.Persistence;

internal sealed record AccountChallengeRecord(
    Guid ChallengeId,
    byte Purpose,
    string NormalizedEmail,
    string CredentialHash,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    int AttemptCount,
    int MaxAttempts,
    int Version,
    DateTimeOffset CreatedAtUtc);
