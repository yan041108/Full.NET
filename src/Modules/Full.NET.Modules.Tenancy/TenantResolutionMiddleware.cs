using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        ITenantResolver resolver,
        CurrentTenantAccessor currentTenant,
        IApiResultMapper resultMapper)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var host = httpContext.Request.Host.Host;
        var tenant = string.IsNullOrWhiteSpace(host)
            ? null
            : await resolver
                .ResolveByDomainAsync(host, httpContext.RequestAborted)
                .ConfigureAwait(false);
        if (tenant is null || !tenant.IsActive)
        {
            var result = Result<object?>.Failure(new Error(
                "tenancy.host-not-found",
                "No active tenant is configured for this host.",
                ErrorType.NotFound));
            await resultMapper
                .Map(result, httpContext)
                .ExecuteAsync(httpContext)
                .ConfigureAwait(false);
            return;
        }

        httpContext.Items["FullNet.TenantId"] = tenant.Id;
        currentTenant.SetTenant(new TenantContext(
            tenant.Id,
            tenant.Identifier,
            tenant.Name));
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}
