using System.Text.Json.Serialization;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 更新当前认证账号语言偏好的传输契约。
/// </summary>
/// <remarks>
/// 拒绝未映射成员，避免调用方误以为 UserId、TenantId 或 ScopeKey 等字段会参与授权。
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateLocaleRequest(string Locale, int ProfileVersion);
