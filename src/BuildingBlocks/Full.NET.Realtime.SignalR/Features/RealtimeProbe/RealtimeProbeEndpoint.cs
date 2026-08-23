using Full.NET.Realtime;
using Full.NET.Realtime.SignalR.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Realtime.SignalR.Features.RealtimeProbe;

/// <summary>
/// 仅 Testing 环境暴露的发布探针，验证 <see cref="IRealtimePublisher"/> 接线。
/// </summary>
internal static class RealtimeProbeEndpoint
{
    public static void Map(
        IEndpointRouteBuilder endpoints,
        IWebHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing"))
        {
            return;
        }

#if !FULLNET_AOT_ANALYSIS
        endpoints.MapPost(
                "/api/v1/realtime/probes/self",
                HandleProbeAsync)
            .WithTags("Realtime")
            .RequireAuthorization();
#endif
    }

#if !FULLNET_AOT_ANALYSIS
    private static async Task<Results<UnauthorizedHttpResult, Ok<RealtimeProbeResponse>>>
        HandleProbeAsync(
            HttpContext httpContext,
            IRealtimePublisher publisher,
            IOptions<RealtimeOptions> realtimeOptions,
            CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(httpContext, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        await publisher.PublishToUserAsync(
                userId,
                new RealtimeMessage(
                    RealtimeMessageCodes.ProbeSelf,
                    new Dictionary<string, object?>
                    {
                        ["hubPath"] = realtimeOptions.Value.HubPath,
                    }),
                cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(
            new RealtimeProbeResponse(RealtimeMessageCodes.ProbeSelf));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
#endif
}
