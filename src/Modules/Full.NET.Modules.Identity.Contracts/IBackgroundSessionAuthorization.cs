namespace Full.NET.Modules.Identity.Contracts;

/// <summary>无 HTTP 上下文时按冻结会话绑定复核权限；不支持 API Key 或持久委托替代会话。</summary>
public interface IBackgroundSessionAuthorization
{
    /// <summary>会话失效、租户切换或权限缺失时返回空。</summary>
    Task<AuthorizedSessionActor?> AuthorizeAsync(
        SessionBindingSnapshot binding,
        string permissionCode,
        CancellationToken cancellationToken = default);
}
