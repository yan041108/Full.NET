namespace Full.NET.Modules.Identity.Contracts;

/// <summary>复核当前 API Key 或签名凭据的权威状态，供跨模块长操作在提交响应前再次授权。</summary>
/// <remarks>仅接受当前认证处理器建立的主体和可信范围；不执行二次认证、签名重放或配额计费，也不接受调用方传入密钥或租户。</remarks>
public interface ICurrentApiKeyAuthorization
{
    /// <summary>重新核对凭据、账号、有效期、安全戳、范围及全部精确权限。</summary>
    /// <param name="permissions">本操作所需的非空精确权限集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>权威授权通过时返回当前主体标识；无效凭据或权限返回空值。</returns>
    Task<Guid?> AuthorizeAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);
}
