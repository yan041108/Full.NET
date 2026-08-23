using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.GetDeadLetters;

internal static class Endpoint
{
    /// <summary>
    /// 注册消费死信查询路由，分页返回死信列表，可按消费者名过滤。
    /// </summary>
    /// <remarks>绑定 <c>dead_letters.read</c> 权限，供运维排查消费失败与重放决策。</remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/messaging/dead-letters")
            .WithTags("Messaging");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? consumerName,
            DeadLetterQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    consumerName,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<PagedResult<DeadLetterResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeadLettersRead));
    }
}
