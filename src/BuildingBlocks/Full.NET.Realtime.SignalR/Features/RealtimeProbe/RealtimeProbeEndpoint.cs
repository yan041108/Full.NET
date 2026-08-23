using Full.NET.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
        if (!string.Equals(
                environment.EnvironmentName,
                "Testing",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        endpoints.MapPost(
                "/api/v1/realtime/probes/self",
                HandleProbeAsync)
            .WithTags("Realtime")
            .RequireAuthorization();
    }

    private static async Task HandleProbeAsync(HttpContext httpContext)
    {
        if (!TryResolveUserId(httpContext, out var userId))
        {
            await Results.Unauthorized().ExecuteAsync(httpContext);
            return;
        }

        var publisher = httpContext.RequestServices.GetRequiredService<IRealtimePublisher>();
        var realtimeOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<RealtimeOptions>>()
            .Value;

        await publisher.PublishToUserAsync(
                userId,
                new RealtimeMessage(
                    RealtimeMessageCodes.ProbeSelf,
                    new Dictionary<string, object?>
                    {
                        ["probeId"] = Guid.CreateVersion7(),
                        ["hubPath"] = realtimeOptions.HubPath,
                        ["sequence"] = 1L,
                    }),
                httpContext.RequestAborted)
            .ConfigureAwait(false);

        await Results.Ok(new RealtimeProbeResponse(RealtimeMessageCodes.ProbeSelf))
            .ExecuteAsync(httpContext);
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
