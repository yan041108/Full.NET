namespace Full.NET.Modules.Identity.Contracts;

/// <summary>平台认证事件查询权限。</summary>
public static class AuthenticationEventPermissions
{
    public const string Read = "identity.authentication_events.read";
    public const string Export = "identity.authentication_events.export";
}

/// <summary>认证事件的安全投影；不返回用户名指纹、IP、User-Agent 或任何凭据。</summary>
public sealed record AuthenticationEventResponse(
    Guid Id,
    Guid? UserId,
    Guid? SessionId,
    string EventType,
    string ResultCode,
    bool Succeeded,
    Guid? ContextTenantId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorUserId,
    string? TraceId,
    string? AuthenticationMethod,
    string? ClientId,
    Guid? CenterSessionId,
    Guid? ApplicationSessionId);
