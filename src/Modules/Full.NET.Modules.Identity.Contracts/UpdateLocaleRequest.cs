using System.Text.Json.Serialization;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 更新当前认证账号语言偏好的传输契约。
/// </summary>
/// <remarks>
/// 拒绝未映射成员，避免调用方误以为 UserId、TenantId 或 ScopeKey 等字段会参与授权。
/// </remarks>
/// <param name="Locale">按 BCP 47 提交的目标语言标签；服务端会按受支持语言目录再做校验。</param>
/// <param name="ProfileVersion">调用方看到的档案版本，用于避免并发覆盖其他账号资料修改。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateLocaleRequest(string Locale, int ProfileVersion);
