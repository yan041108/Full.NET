using Microsoft.AspNetCore.Builder;

namespace Full.NET.Modules.Tenancy;

public static class TenancyApplicationBuilderExtensions
{
    public static IApplicationBuilder UseFullNetTenancy(
        this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
