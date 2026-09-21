using Full.NET.Abstractions.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Jobs.Middleware;

/// <summary>
/// Host 作业 API 使用 HostOnly SQL；超级管理员在租户上下文中调用时需临时绑定 Host 作用域。
/// </summary>
internal sealed class HostJobsHostContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(
        HttpContext context,
        ICurrentTenant currentTenant,
        ICurrentTenantContextWriter tenantWriter)
    {
        if (!IsHostJobsApiPath(context.Request.Path))
        {
            return next(context);
        }

        return HostJobsHostContextScope.RunAsync(
            currentTenant,
            tenantWriter,
            () => next(context));
    }

    private static bool IsHostJobsApiPath(PathString path) =>
        path.StartsWithSegments("/api/v1/jobs/host", StringComparison.OrdinalIgnoreCase);
}
