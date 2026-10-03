using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features;

/// <summary>交互及凭据读取均核对权威权限，长操作响应前可再次调用。</summary>
/// <param name="authorization">权威交互会话授权。</param>
/// <param name="contexts">当前已认证 HTTP 主体。</param>
/// <param name="credentials">当前 API Key 或签名凭据的权威授权。</param>
internal sealed class AiKnowledgePermissionGuard(ICurrentSessionAuthorization authorization, IHttpContextAccessor contexts, ICurrentApiKeyAuthorization credentials)
{
    internal async Task<bool> IsAuthorizedAsync(IReadOnlyList<string> permissions, Guid actor, CancellationToken token)
    {
        var principal = contexts.HttpContext?.User;
        // 只接受认证处理器建立的凭据身份类型，不能仅凭某个 JWT 声明跳过权威会话校验。
        if (principal?.Identity is { IsAuthenticated: true, AuthenticationType: "FullNET.ApiKey" or "FullNET.Signature" }
            && principal.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.ApiKeyId))
            return await credentials.AuthorizeAsync(permissions, token).ConfigureAwait(false) == actor;
        foreach (var permission in permissions)
            if ((await authorization.AuthorizeAsync(permission, token).ConfigureAwait(false))?.UserId != actor) return false;
        return true;
    }
}

/// <summary>资源读取保持既有策略门禁，并再次核对交互会话权威权限。</summary>
/// <param name="permissions">本端点必需的精确权限集合。</param>
internal sealed class AiKnowledgeReadPermissionFilter(IReadOnlyList<string> permissions) : IEndpointFilter
{
    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!Guid.TryParse(http.User.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty) return Results.Unauthorized();
        if (!await http.RequestServices.GetRequiredService<AiKnowledgePermissionGuard>().IsAuthorizedAsync(permissions, actor, http.RequestAborted).ConfigureAwait(false))
            return http.RequestServices.GetRequiredService<IApiResultMapper>().Map(Result<bool>.Failure(new Error(
                "authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden)), http);
        return await next(context).ConfigureAwait(false);
    }
}
