using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.MyTenantRegistrations;

/// <summary>端点只从已验证会话读取用户，不接受账号、企业、成员或注册方式作为操作目标。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me/tenant-registrations", async (HttpContext context,
            MyTenantRegistrationService service, IApiResultMapper mapper, CancellationToken token) =>
            Guid.TryParse(context.User.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var userId)
                ? mapper.Map(await service.ListAsync(userId, token).ConfigureAwait(false), context) : Results.Unauthorized())
            .WithTags("IdentityMe").WithName("identityListMyTenantRegistrations")
            .Produces<IReadOnlyList<SelfTenantRegistrationResponse>>()
            .RequireFullNetPermission(IdentitySelfTenantRegistrationPermissions.Read);
        endpoints.MapPost("/api/v1/me/tenant-registrations/{id:guid}/continue", async (Guid id,
            ContinueTenantRegistrationRequest request, HttpContext context, MyTenantRegistrationService service,
            IApiResultMapper mapper, CancellationToken token) =>
            Guid.TryParse(context.User.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var userId)
                ? mapper.Map(await service.ContinueAsync(userId, id, request.Version, token).ConfigureAwait(false), context) : Results.Unauthorized())
            .WithTags("IdentityMe").WithName("identityContinueMyTenantRegistration")
            .Produces<SelfTenantRegistrationResponse>()
            .RequireFullNetPermission(IdentitySelfTenantRegistrationPermissions.Continue);
    }
}
