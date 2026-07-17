using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 提供显式、幂等的首个宿主管理员引导能力。
/// </summary>
public interface IIdentityBootstrapService
{
    /// <summary>
    /// 创建首个宿主管理员；账号已存在时保持原密码并返回现有标识。
    /// </summary>
    Task<Result<BootstrapHostAdminResult>> BootstrapHostAdminAsync(
        BootstrapHostAdminRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 首个宿主管理员的显式引导输入。
/// </summary>
public sealed record BootstrapHostAdminRequest(
    string Username,
    string Password,
    string DisplayName);

/// <summary>
/// 宿主管理员引导结果；Created 表示本次是否实际插入账号。
/// </summary>
public sealed record BootstrapHostAdminResult(Guid UserId, bool Created);
