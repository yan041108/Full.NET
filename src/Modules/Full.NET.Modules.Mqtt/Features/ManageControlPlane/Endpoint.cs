using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Mqtt.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Mqtt.Features.ManageControlPlane;

/// <summary>MQTT 控制面 HTTP 端点：Broker 状态、客户端目录、消息记录与受控发布。</summary>
internal static class Endpoint
{
    /// <summary>注册 MQTT 控制面路由。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/mqtt")
            .WithTags("MqttControlPlane");

        group.MapGet("/status", (
            MqttBrokerStatusService statusService) =>
            Results.Ok(statusService.GetStatus()))
        .WithName("mqttGetBrokerStatus")
        .Produces<MqttBrokerStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(MqttPermissions.BrokerRead));

        group.MapGet("/clients", async (
            MqttClientQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("mqttListClients")
        .Produces<IReadOnlyList<MqttClientResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(MqttPermissions.ClientsRead));

        group.MapGet("/clients/{clientId:guid}", async (
            Guid clientId,
            MqttClientQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(clientId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("mqttGetClient")
        .Produces<MqttClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(MqttPermissions.ClientsRead));

        group.MapGet("/messages", async (
            int? page,
            int? pageSize,
            Guid? clientId,
            string? status,
            string? topic,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            MqttMessageQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    new MqttMessageListFilter(clientId, status, topic, fromUtc, toUtc),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("mqttListMessages")
        .Produces<PagedResult<MqttMessageResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(MqttPermissions.MessagesRead));

        group.MapGet("/messages/{messageId:guid}", async (
            Guid messageId,
            MqttMessageQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(messageId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("mqttGetMessage")
        .Produces<MqttMessageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(MqttPermissions.MessagesRead));

        group.MapPost("/messages/publish", async (
            PublishMqttMessageRequest request,
            MqttMessagePublishService publishService,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(httpContext, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await publishService.PublishAsync(
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("mqttPublishMessage")
        .Produces<MqttMessageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .RequireAuthorization(FullNetPermissionPolicies.For(MqttPermissions.MessagesPublish));
    }

    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
