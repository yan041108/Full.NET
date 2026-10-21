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
    /// <summary>一次权威会话及权限快照读取内检查全部精确权限；每个业务阶段必须重新调用。</summary>
    /// <param name="binding">冻结的会话绑定。</param>
    /// <param name="permissionCodes">非空的必需权限集合，调用开始时冻结。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部权限满足时的主体快照；失效或缺少任一权限时为空。</returns>
    Task<AuthorizedSessionActor?> AuthorizeAllAsync(
        SessionBindingSnapshot binding,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken = default);
}
