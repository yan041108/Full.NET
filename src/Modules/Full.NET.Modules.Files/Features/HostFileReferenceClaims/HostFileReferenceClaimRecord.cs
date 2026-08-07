namespace Full.NET.Modules.Files.Features.HostFileReferenceClaims;

internal sealed record HostFileReferenceClaimRecord(
    Guid Id,
    string IdempotencyKey,
    Guid FileId,
    string ConsumerModule,
    Guid ConsumerReferenceId,
    string State,
    string? ContentHash,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? ReleasedAtUtc);
