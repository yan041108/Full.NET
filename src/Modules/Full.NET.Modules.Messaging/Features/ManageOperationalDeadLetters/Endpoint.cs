using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Messaging.Features.ManageOperationalDeadLetters;

/// <summary>两个来源分别路由；写操作独立权限，客户端不能选择任意数据库表或消费者。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        MapSource(endpoints, false, "/api/v1/messaging/outbox/dead-letters", "messagingLegacy", MessagingPermissions.LegacyDeadLettersRequeue, "requeue");
        MapSource(endpoints, true, "/api/v1/messaging/kafka/dead-letters", "messagingKafka", MessagingPermissions.DeadLettersReplay, "replay");
    }

    private static void MapSource(IEndpointRouteBuilder endpoints, bool isKafka, string path, string name, string permission, string action)
    {
        var group = endpoints.MapGroup(path).WithTags("MessagingDeadLetters");
        group.MapGet("", async (int? page, int? pageSize, string? messageType, int? schemaVersion,
            string? errorCode, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? consumerName, OperationalDeadLetterService service,
            IApiResultMapper mapper, HttpContext http, CancellationToken cancellationToken) =>
                mapper.Map(await service.ListAsync(isKafka, new DeadLetterFilter(page ?? 1, pageSize ?? 20,
                    messageType, schemaVersion, errorCode, fromUtc, toUtc, consumerName), cancellationToken), http))
            .WithName(name + "ListDeadLetters").Produces<PagedResult<OperationalDeadLetterResponse>>()
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeadLettersRead));
        group.MapGet("/{id:guid}", async (Guid id, OperationalDeadLetterService service,
            IApiResultMapper mapper, HttpContext http, CancellationToken cancellationToken) =>
                mapper.Map(await service.GetAsync(isKafka, id, cancellationToken), http))
            .WithName(name + "GetDeadLetter").Produces<OperationalDeadLetterResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(MessagingPermissions.DeadLettersRead));
        group.MapPost("/{id:guid}/" + action, async (Guid id, RecoverDeadLetterRequest request,
            OperationalDeadLetterService service, IApiResultMapper mapper, HttpContext http, CancellationToken cancellationToken) =>
                mapper.Map(await service.RecoverAsync(isKafka, id, request, cancellationToken), http))
            .WithName(name + "RecoverDeadLetter").Produces<RecoverDeadLetterResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(permission));
    }
}
