/// <summary>Files 持久化行投影；仅供 Dapper 物化，不直接作为 HTTP 契约。</summary>
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
