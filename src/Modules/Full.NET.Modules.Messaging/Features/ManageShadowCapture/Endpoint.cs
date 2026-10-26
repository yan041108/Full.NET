using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.ManageShadowCapture;

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/messaging/delivery/shadow-captures").WithTags("Messaging");
        group.MapGet("/current", async (string eventType, int schemaVersion, ShadowCaptureService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => mapper.Map(await service.FindAsync(eventType, schemaVersion, token).ConfigureAwait(false), context))
            .WithName("messagingGetCurrentShadowCapture").Produces<ShadowCaptureResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.EventsRead));
        group.MapPost("/", async (StartShadowCaptureRequest request, ShadowCaptureService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => mapper.Map(await service.StartAsync(request, token).ConfigureAwait(false), context))
            .WithName("messagingStartShadowCapture").Produces<ShadowCaptureResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeliveryCutover));
        group.MapPost("/{generation:guid}/close", async (Guid generation, CloseShadowCaptureRequest request, ShadowCaptureService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => mapper.Map(await service.CloseAsync(generation, request, token).ConfigureAwait(false), context))
            .WithName("messagingCloseShadowCapture").Produces<ShadowCaptureResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeliveryCutover));
    }
}
