namespace Full.NET.Modules.Identity.Contracts;

/// <summary>无 HTTP 上下文时按冻结会话绑定复核权限；不支持 API Key 或持久委托替代会话。</summary>
public interface IBackgroundSessionAuthorization
{
    /// <summary>按冻结会话绑定与权限码复核授权。</summary>
    /// <param name="binding">冻结的会话绑定快照。</param>
    /// <param name="permissionCode">所需的精确权限码。</param>
    /// <param name="cancellationToken">取消授权校验的令牌。</param>
    /// <returns>授权成功的最小主体快照；会话失效、租户切换或权限缺失时为 <see langword="null"/>。</returns>
    Task<AuthorizedSessionActor?> AuthorizeAsync(
        SessionBindingSnapshot binding,
        string permissionCode,
        CancellationToken cancellationToken = default);
}
