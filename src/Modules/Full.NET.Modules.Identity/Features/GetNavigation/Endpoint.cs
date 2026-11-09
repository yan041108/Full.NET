using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Navigation读取 API 路由注册。</summary>
namespace Full.NET.Modules.Identity.Features.GetNavigation;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var oidcCenterEnabled = endpoints.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetValue<bool>($"{IdentityOidcOptions.SectionName}:Enable");
        endpoints.MapGet(
                "/api/v1/navigation",
                async (
                    ClaimsPrincipal principal,
                    PermissionClaimEvaluator permissionClaimEvaluator,
                    NavigationProjector projector,
                    HostNavigationDefinitionLoader navigationLoader,
                    [FromServices] ICurrentTenant currentTenant,
                    CancellationToken cancellationToken) =>
                {
                    var permissions = permissionClaimEvaluator.ResolvePermissions(principal);
                    var additionalDefinitions = await navigationLoader
                        .LoadActiveDefinitionsAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var isHostDataContext = currentTenant.IsAvailable && currentTenant.IsHost;
                    return Results.Ok(projector.Project(
                        permissions,
                        isHostDataContext,
                        additionalDefinitions,
                        oidcCenterEnabled));
                })
            .WithTags("Identity")
            .RequireFullNetPermission(IdentityAuthorizationContributor.NavigationRead);
    }
}
