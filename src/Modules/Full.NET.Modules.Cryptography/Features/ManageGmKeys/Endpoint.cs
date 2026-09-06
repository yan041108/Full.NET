using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Cryptography.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Cryptography.Features.ManageGmKeys;

/// <summary>国密控制面 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册国密控制面路由。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/cryptography")
            .WithTags("CryptographyGmKeys");

        group.MapGet("/status", (CryptographyStatusService statusService) =>
            Results.Ok(statusService.GetStatus()))
        .WithName("cryptographyGetStatus")
        .Produces<CryptographyStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(CryptographyPermissions.KeysRead));

        group.MapGet("/keys", async (
            CryptographyKeyQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("cryptographyListKeys")
        .Produces<IReadOnlyList<CryptographyKeyResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(CryptographyPermissions.KeysRead));

        group.MapGet("/keys/{keyId:guid}", async (
            Guid keyId,
            CryptographyKeyQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(keyId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("cryptographyGetKey")
        .Produces<CryptographyKeyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(CryptographyPermissions.KeysRead));

        group.MapPost("/sm2/sign", async (
            Sm2SignRequest request,
            Sm2SignatureService signatureService,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await signatureService.SignAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("cryptographySm2Sign")
        .Produces<Sm2SignResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(CryptographyPermissions.Sm2Sign));

        group.MapPost("/sm2/verify", async (
            Sm2VerifyRequest request,
            Sm2VerifyService verifyService,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await verifyService.VerifyAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("cryptographySm2Verify")
        .Produces<Sm2VerifyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(CryptographyPermissions.Sm2Verify));
    }
}
