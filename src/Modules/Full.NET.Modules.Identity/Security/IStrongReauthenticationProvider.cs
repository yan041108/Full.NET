using Full.NET.Abstractions.Results;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 超级管理员远程写操作的强认证入口；Production 仅接受 <see cref="IsProductionEligible"/> 为 true 的实现。
/// </summary>
internal interface IStrongReauthenticationProvider
{
    /// <summary>是否满足 ADR-0004 的 Production 合格条件。</summary>
    bool IsProductionEligible { get; }

    /// <summary>
    /// 校验操作者当前密码及（若 Provider 要求）TOTP，成功时返回 Host 用户。
    /// </summary>
    Task<Result<IdentityUser>> VerifyAsync(
        Guid operatorUserId,
        string currentPassword,
        string? totpCode,
        CancellationToken cancellationToken = default);
}
