using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.ReplayDeadLetter;

internal static class Endpoint
{
    /// <summary>
    /// 注册消费死信重放路由，按消费者名与消息标识重放单条死信。
    /// </summary>
    /// <remarks>
    /// 绑定 <c>dead_letters.replay</c> 权限；重放依赖消费 Inbox 幂等，
    /// 重复重放不会产生重复业务写入。
    /// </remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/messaging/dead-letters")
            .WithTags("Messaging");

        group.MapPost("/replay", async (
            ReplayDeadLetterRequest request,
            DeadLetterReplayService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReplayAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<DeadLetterReplayResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeadLettersReplay));
    }
}
