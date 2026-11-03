namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Tenancy 切换上下文后将新令牌交回 Identity 的服务端浏览器票据，不向浏览器返回凭据。</summary>
public interface IIdentityBrowserSession
{
    /// <summary>本次请求是否通过服务端浏览器票据认证。</summary>
    bool IsActive { get; }

    /// <summary>保存上下文切换后的凭据；并发变更失败时要求重新恢复权威状态。</summary>
    Task ReplaceCredentialsAsync(TenantContextTokenResponse credentials, CancellationToken cancellationToken);
}
