using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/me/mfa/recovery-codes")
            .WithTags("IdentityMfaRecoveryCodes")
            .RequireAuthorization();

        group.MapPost("/regenerate", async (
            ClaimsPrincipal principal,
            MfaRecoveryCodeService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var userId = ResolveUserId(principal);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var result = await service.RegenerateAsync(userId.Value, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRegenerateMfaRecoveryCodes")
        .Produces<RegenerateMfaRecoveryCodesResponse>(StatusCodes.Status200OK);

        group.MapPost("/consume", async (
            ConsumeMfaRecoveryCodeRequest request,
            ClaimsPrincipal principal,
            MfaRecoveryCodeService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var userId = ResolveUserId(principal);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var result = await service.ConsumeAsync(userId.Value, request.RecoveryCode, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityConsumeMfaRecoveryCode")
        .Produces<ConsumeMfaRecoveryCodeResponse>(StatusCodes.Status200OK);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
