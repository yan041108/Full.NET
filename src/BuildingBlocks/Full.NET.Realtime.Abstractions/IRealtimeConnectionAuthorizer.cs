using System.Security.Claims;

namespace Full.NET.Realtime;

/// <summary>长连接使用独立服务作用域重新读取权威会话状态，不沿用连接建立时的授权结果。</summary>
public interface IRealtimeConnectionAuthorizer
{
    /// <summary>验证原连接身份是否仍可接收其作用域的业务推送；未知状态必须拒绝。</summary>
    /// <param name="principal">连接建立时已验证的身份。</param>
    /// <param name="cancellationToken">本次权威读取的期限。</param>
    Task<bool> IsAuthorizedAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
