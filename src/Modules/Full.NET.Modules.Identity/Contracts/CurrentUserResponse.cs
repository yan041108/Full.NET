namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 当前认证用户可安全公开给客户端的身份摘要。
/// </summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    Guid? TenantId,
    string Scope,
    IReadOnlyCollection<string> Permissions,
    Guid SessionId);
