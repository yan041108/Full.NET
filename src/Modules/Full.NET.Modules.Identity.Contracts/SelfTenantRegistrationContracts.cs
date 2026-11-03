using System.Text.Json.Serialization;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>统一账号管理本人注册入驻意图的精确权限。</summary>
public static class IdentitySelfTenantRegistrationPermissions
{
    /// <summary>查看本人通过开放注册创建的入驻意图。</summary>
    public const string Read = "identity.tenant_registrations.read_self";
    /// <summary>继续本人未完成、且仍被原配置允许的入驻意图。</summary>
    public const string Continue = "identity.tenant_registrations.continue_self";
}

/// <summary>仅暴露本人可操作的入驻进度，不包含内部租约、账号或请求摘要。</summary>
/// <param name="Id">入驻操作标识。</param>
/// <param name="TenantId">目标企业标识。</param>
/// <param name="RegistrationWayName">注册方式名称。</param>
/// <param name="Phase">既有席位编排的稳定阶段。</param>
/// <param name="ErrorCode">最近一次稳定失败码。</param>
/// <param name="CreatedAtUtc">账号发起注册的时间。</param>
/// <param name="Version">并发版本。</param>
/// <param name="CanContinue">当前阶段是否提供显式继续入口；服务端仍重新校验原成员版本。</param>
public sealed record SelfTenantRegistrationResponse(Guid Id, Guid TenantId, string? RegistrationWayName,
    string Phase, string? ErrorCode, DateTimeOffset CreatedAtUtc, int Version, bool CanContinue);

/// <summary>继续操作必须锚定用户刚查看的意图版本。</summary>
/// <param name="Version">最后读取的入驻意图版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContinueTenantRegistrationRequest(int Version);
