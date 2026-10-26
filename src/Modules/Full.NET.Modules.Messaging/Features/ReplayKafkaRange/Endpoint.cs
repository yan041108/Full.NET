using Full.NET.Hosting.Api;
using Full.NET.Abstractions.Results;
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
        var operations = endpoints.MapGroup("/api/v1/messaging/kafka/replay-operations").WithTags("Messaging");
        operations.MapPost("/", async (CreateKafkaReplayOperationRequest request, KafkaReplayOperationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            var result = await service.CreateAsync(request, token).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created($"/api/v1/messaging/kafka/replay-operations/{result.Value!.OperationId}", result.Value)
                : mapper.Map(result, context);
        }).WithName("messagingCreateReplayOperation").Produces<KafkaReplayOperationResponse>(StatusCodes.Status201Created)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.KafkaRangeReplay));
        operations.MapGet("/", async (int? page, int? pageSize, KafkaReplayOperationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.ListAsync(page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context))
            .WithName("messagingListReplayOperations").Produces<PagedResult<KafkaReplayOperationResponse>>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.EventsRead));
        operations.MapGet("/{id:guid}", async (Guid id, KafkaReplayOperationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.FindAsync(id, token).ConfigureAwait(false), context))
            .WithName("messagingGetReplayOperation").Produces<KafkaReplayOperationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.EventsRead));
        operations.MapPost("/{id:guid}/execute", async (Guid id, ChangeKafkaReplayOperationRequest request, KafkaReplayOperationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.ExecuteAsync(id, request, token).ConfigureAwait(false), context))
            .WithName("messagingExecuteReplayOperation").Produces<KafkaReplayOperationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.KafkaRangeReplay));
        operations.MapPost("/{id:guid}/cancel", async (Guid id, ChangeKafkaReplayOperationRequest request, KafkaReplayOperationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.CancelAsync(id, request, token).ConfigureAwait(false), context))
            .WithName("messagingCancelReplayOperation").Produces<KafkaReplayOperationResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.KafkaRangeReplay));
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
        .WithName("messagingReplayKafkaRange")
        .Produces<KafkaRangeReplayResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            MessagingPermissions.KafkaRangeReplay));
    }
}
