using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ocr.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ocr.Features.ManageProviderConfigs;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ocr/provider-configs")
            .WithTags("OcrProviderConfigs");

        group.MapGet("/{providerKey}", async (
            string providerKey,
            OcrProviderQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByKeyAsync(providerKey, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("ocrGetProviderConfig")
        .Produces<OcrProviderConfigResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrProviderPermissions.Read));

        group.MapPut("/{providerKey}", async (
            string providerKey,
            UpdateOcrProviderConfigRequest request,
            OcrProviderManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(providerKey, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("ocrUpdateProviderConfig")
        .Produces<OcrProviderConfigResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrProviderPermissions.Update));

        group.MapPost("/{providerKey}/test", async (
            string providerKey,
            OcrProviderOperationsService operations,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await operations.TestAsync(providerKey, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("ocrTestProviderConfig")
        .Produces<TestOcrProviderConfigResult>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OcrProviderPermissions.Test));
    }
}
