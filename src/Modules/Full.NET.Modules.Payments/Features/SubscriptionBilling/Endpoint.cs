using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>付款仅引用已有冻结报价；重试只接续原事实，不接受覆盖金额或商户。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/payments/subscriptions/{tenantId:guid}/{subscriptionId:guid}/checkout", async (
            Guid tenantId, Guid subscriptionId, SubscriptionCheckoutService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) =>
            mapper.Map(await service.CheckoutAsync(tenantId, subscriptionId, token).ConfigureAwait(false), context))
            .WithName("paymentsCheckoutSubscription").WithTags("PaymentSubscriptionFulfillment")
            .Produces<SubscriptionCheckoutResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(PaymentOrderPermissions.Create),
                FullNetPermissionPolicies.For(TenancyTenantSubscriptionPermissions.Manage));
        endpoints.MapGet("/api/v1/payments/orders/{orderId:guid}/subscription-fulfillment", async (
            Guid orderId, SubscriptionCheckoutService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.StatusAsync(orderId, token).ConfigureAwait(false), context))
            .WithName("paymentsGetSubscriptionFulfillment").WithTags("PaymentSubscriptionFulfillment")
            .Produces<SubscriptionFulfillmentStatusResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(PaymentOrderPermissions.Read),
                FullNetPermissionPolicies.For(TenancyTenantSubscriptionPermissions.Read));
        endpoints.MapPost("/api/v1/payments/orders/{orderId:guid}/subscription-fulfillment/retry", async (
            Guid orderId, SubscriptionFulfillmentProcessor processor, SubscriptionCheckoutService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            await processor.RetryAsync(orderId, token).ConfigureAwait(false);
            return mapper.Map(await service.StatusAsync(orderId, token).ConfigureAwait(false), context);
        }).WithName("paymentsRetrySubscriptionFulfillment").WithTags("PaymentSubscriptionFulfillment")
            .Produces<SubscriptionFulfillmentStatusResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(PaymentOrderPermissions.Reconcile),
                FullNetPermissionPolicies.For(TenancyTenantSubscriptionPermissions.Manage));
    }
}
