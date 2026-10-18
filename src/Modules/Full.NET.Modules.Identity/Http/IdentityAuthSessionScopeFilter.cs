using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Http;

/// <summary>会话控制使用 Cookie 确定会话族，拒绝租户作用域进入 HostOnly 数据操作。</summary>
internal sealed class IdentityAuthSessionScopeFilter(ICurrentTenant currentTenant, IApiResultMapper mapper) : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (currentTenant.IsHost && currentTenant.Id is null)
        {
            return next(context);
        }

        // 不重置可信租户上下文，也不撤销或删除被拒绝请求携带的会话 Cookie。
        return ValueTask.FromResult<object?>(mapper.Map(
            Result<object?>.Failure(new Error(
                CommonErrorCodes.PermissionDenied,
                "The host context is required for session control.",
                ErrorType.Forbidden)),
            context.HttpContext));
    }
}
