namespace Full.NET.Modules.Identity.Domain;

internal sealed record AuthAuditEvent(
    Guid Id,
    Guid? UserId,
    Guid? SessionId,
    string UsernameFingerprint,
    string EventType,
    string ResultCode,
    bool Succeeded,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset OccurredAtUtc);
