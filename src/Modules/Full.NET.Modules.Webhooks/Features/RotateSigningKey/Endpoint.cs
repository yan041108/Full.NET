using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Webhooks.Features.RotateSigningKey;

/// <summary>轮换读取与写入分权；每次变更都经原会话及提交前权威复核。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/webhooks/subscriptions/{subscriptionId:guid}/signing-key-rotations").WithTags("Webhooks");
        group.MapGet("/pending",async(Guid subscriptionId,WebhookSigningKeyRotationService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.ReadAsync(subscriptionId,null,token).ConfigureAwait(false),context))
            .WithName("webhooksGetPendingSigningKeyRotation").Produces<WebhookSigningKeyRotationReceipt>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsRead));
        group.MapGet("/{operationId:guid}",async(Guid subscriptionId,Guid operationId,WebhookSigningKeyRotationService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.ReadAsync(subscriptionId,operationId,token).ConfigureAwait(false),context))
            .WithName("webhooksGetSigningKeyRotation").Produces<WebhookSigningKeyRotationReceipt>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsRead));
        Write(group.MapPost("/",async(Guid subscriptionId,PrepareWebhookSigningKeyRotationRequest request,WebhookSigningKeyRotationService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.PrepareAsync(subscriptionId,request,token).ConfigureAwait(false),context)).WithName("webhooksPrepareSigningKeyRotation"));
        Write(group.MapPost("/{operationId:guid}/activate",async(Guid subscriptionId,Guid operationId,CompleteWebhookSigningKeyRotationRequest request,WebhookSigningKeyRotationService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.CompleteAsync(subscriptionId,operationId,request,true,token).ConfigureAwait(false),context)).WithName("webhooksActivateSigningKeyRotation"));
        Write(group.MapPost("/{operationId:guid}/cancel",async(Guid subscriptionId,Guid operationId,CompleteWebhookSigningKeyRotationRequest request,WebhookSigningKeyRotationService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.CompleteAsync(subscriptionId,operationId,request,false,token).ConfigureAwait(false),context)).WithName("webhooksCancelSigningKeyRotation"));
    }
    private static void Write(RouteHandlerBuilder route)=>route.Produces<WebhookSigningKeyRotationReceipt>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503)
        .RequireAuthorization(FullNetPermissionPolicies.For(WebhookPermissions.SubscriptionsRead),FullNetPermissionPolicies.For(WebhookPermissions.SigningKeysRotate));
}
