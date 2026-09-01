using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 提供显式、幂等的首个宿主管理员引导能力。
/// </summary>
public interface IIdentityBootstrapService
{
    /// <summary>
    /// 创建首个宿主管理员并同步系统授权；账号已存在时保持原密码。
    /// </summary>
    /// <param name="request">显式提供的宿主管理员初始资料；调用方必须确保密码来自安全 Secret 而非硬编码。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>返回引导后的账号标识以及本次是否真正创建或修复授权关系。</returns>
    Task<Result<BootstrapHostAdminResult>> BootstrapHostAdminAsync(
        BootstrapHostAdminRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 首个宿主管理员的显式引导输入。
/// </summary>
/// <param name="Username">宿主管理员登录名。</param>
/// <param name="Password">首次引导使用的明文密码；只允许存在于当前引导请求边界。</param>
/// <param name="DisplayName">管理端展示名称。</param>
public sealed record BootstrapHostAdminRequest(
    string Username,
    string Password,
    string DisplayName);

/// <summary>
/// 宿主管理员引导结果；同时报告账号创建和系统授权同步结果。
/// </summary>
/// <param name="UserId">完成引导的宿主管理员标识。</param>
/// <param name="Created">本次是否新建了管理员账号。</param>
/// <param name="AuthorizationSynchronized">系统角色与权限是否已同步完成。</param>
public sealed record BootstrapHostAdminResult(
    Guid UserId,
    bool Created,
    bool AuthorizationSynchronized)
{
    /// <summary>获取本次是否修复了系统角色或账号角色关系。</summary>
    public bool AuthorizationChanged { get; init; }
}
