using Full.NET.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Realtime.SignalR.Features.RealtimeProbe;

/// <summary>
/// 仅 Testing 环境暴露的发布探针，验证 <see cref="IRealtimePublisher"/> 接线。
/// </summary>
internal static class RealtimeProbeEndpoint
{
    public static void Map(
        IEndpointRouteBuilder endpoints,
        IWebHostEnvironment environment,
        string hubPath)
    {
        if (!environment.IsEnvironment("Testing"))
        {
            return;
        }

        endpoints.MapPost(
                "/api/v1/realtime/probes/self",
                async (
                    HttpContext httpContext,
                    IRealtimePublisher publisher,
                    CancellationToken cancellationToken) =>
                {
                    if (!TryResolveUserId(httpContext, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    await publisher.PublishToUserAsync(
                            userId,
                            new RealtimeMessage(
                                RealtimeMessageCodes.ProbeSelf,
                                new Dictionary<string, object?>
                                {
                                    ["hubPath"] = hubPath
                                }),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Results.Ok(new { code = RealtimeMessageCodes.ProbeSelf });
                })
            .WithTags("Realtime")
            .RequireAuthorization();
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
