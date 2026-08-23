using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.ReplayKafkaRange;

internal static class Endpoint
{
    /// <summary>
    /// 注册 Kafka 范围重放路由，按时间或偏移量区间重新投递消息。
    /// </summary>
    /// <remarks>
    /// 绑定 <c>kafka.range_replay</c> 权限；同步重放受配置上限约束，超出部分需异步重放，
    /// 重放依赖消费端幂等，并全程写入领域审计终态。
    /// </remarks>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/messaging/kafka/replay", async (
            KafkaRangeReplayRequest request,
            KafkaRangeReplayOperationsService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReplayAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithTags("Messaging")
        .Produces<KafkaRangeReplayResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            MessagingPermissions.KafkaRangeReplay));
    }
}
