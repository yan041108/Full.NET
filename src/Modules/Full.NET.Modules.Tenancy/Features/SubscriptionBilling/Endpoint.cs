using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>Host 报价发布与付费订阅入口；HTTP 不能直接提交“付款成功”或任意资金引用。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tenancy/tenants/{tenantId:guid}/subscriptions/{subscriptionId:guid}/invoice", async (
            Guid tenantId, Guid subscriptionId, TenantSubscriptionInvoiceQueryService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return mapper.Map(await service.GetAsync(tenantId, subscriptionId, token).ConfigureAwait(false), context);
        }).WithName("tenancyGetSubscriptionInvoice").WithTags("TenancySubscriptionBilling")
            .Produces<TenantSubscriptionInvoiceResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(TenancyTenantSubscriptionPermissions.Read));
        endpoints.MapGet("/api/v1/tenancy/tenants/{tenantId:guid}/subscriptions/{subscriptionId:guid}/renewal-preview", async (
            Guid tenantId, Guid subscriptionId, TenantSubscriptionRenewalPreviewService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return mapper.Map(await service.GetAsync(tenantId, subscriptionId, token).ConfigureAwait(false), context);
        }).WithName("tenancyGetSubscriptionRenewalPreview").WithTags("TenancySubscriptionBilling")
            .Produces<TenantSubscriptionRenewalPreviewResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(TenancyTenantSubscriptionPermissions.Read),
                FullNetPermissionPolicies.For(TenancyTenantPackagePermissions.Read));
        endpoints.MapGet("/api/v1/tenancy/tenant-packages/{packageId:guid}/offer", async (
            Guid packageId, TenantPackageOfferService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.GetAsync(packageId, token).ConfigureAwait(false), context))
            .WithName("tenancyGetPackageOffer").WithTags("TenancySubscriptionBilling")
            .Produces<TenantPackageOfferResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(TenancyTenantPackagePermissions.Read));
        endpoints.MapPut("/api/v1/tenancy/tenant-packages/{packageId:guid}/offer", async (
            Guid packageId, UpsertTenantPackageOfferRequest request, TenantPackageOfferService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryActor(context.User, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.SaveAsync(packageId, request, token, actor).ConfigureAwait(false), context);
        }).WithName("tenancySavePackageOffer").WithTags("TenancySubscriptionBilling")
            .Produces<TenantPackageOfferResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(TenancyTenantPackagePermissions.Update),
                FullNetPermissionPolicies.For(TenancyTenantEntitlementPermissions.ManageBindings));
        endpoints.MapPost("/api/v1/tenancy/tenants/{tenantId:guid}/subscriptions/paid", async (
            Guid tenantId, CreatePaidTenantSubscriptionRequest request, TenantSubscriptionBillingService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryActor(context.User, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.CreateAsync(tenantId, request, token, actor).ConfigureAwait(false), context);
        }).WithName("tenancyCreatePaidSubscription").WithTags("TenancySubscriptionBilling")
            .Produces<TenantSubscriptionPaymentTerms>()
            .RequireAuthorization(FullNetPermissionPolicies.For(TenancyTenantSubscriptionPermissions.Manage));
    }

    private static bool TryActor(ClaimsPrincipal principal, out Guid actor) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out actor)
        && actor != Guid.Empty;
}
