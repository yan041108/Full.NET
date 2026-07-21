using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageTotp;

/// <summary>当前认证用户自助管理 TOTP；不开放代他人登记。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/me/mfa/totp")
            .WithTags("Identity")
            .RequireAuthorization();

        group.MapGet("/", async (
            ClaimsPrincipal principal,
            TotpEnrollmentService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetStatusAsync(principal, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        });

        group.MapPost("/begin", async (
            ClaimsPrincipal principal,
            TotpEnrollmentService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.BeginAsync(principal, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        });

        group.MapPost("/confirm", async (
            ConfirmTotpEnrollmentRequest request,
            ClaimsPrincipal principal,
            TotpEnrollmentService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ConfirmAsync(
                    principal,
                    request.TotpCode,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        });
    }
}
