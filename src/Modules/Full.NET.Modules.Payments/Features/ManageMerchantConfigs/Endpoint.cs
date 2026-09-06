using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Payments.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Payments.Features.ManageMerchantConfigs;

/// <summary>支付商户配置 CRUD 与禁用 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册商户配置管理路由。</summary>
    /// <param name="endpoints">路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/payments/merchant-configs")
            .WithTags("PaymentMerchantConfigs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? tenantId,
            string? channelKey,
            string? nameContains,
            bool? isEnabled,
            PaymentMerchantConfigQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    tenantId,
                    channelKey,
                    nameContains,
                    isEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("paymentsListMerchantConfigs")
        .Produces<PagedResult<PaymentMerchantConfigListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(PaymentMerchantPermissions.Read));

        group.MapGet("/{merchantConfigId:guid}", async (
            Guid merchantConfigId,
            PaymentMerchantConfigQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(merchantConfigId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("paymentsGetMerchantConfig")
        .Produces<PaymentMerchantConfigResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(PaymentMerchantPermissions.Read));

        group.MapPost("/", async (
            CreatePaymentMerchantConfigRequest request,
            PaymentMerchantConfigManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/payments/merchant-configs/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("paymentsCreateMerchantConfig")
        .Produces<PaymentMerchantConfigResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(PaymentMerchantPermissions.Create));

        group.MapPut("/{merchantConfigId:guid}", async (
            Guid merchantConfigId,
            UpdatePaymentMerchantConfigRequest request,
            PaymentMerchantConfigManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(merchantConfigId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("paymentsUpdateMerchantConfig")
        .Produces<PaymentMerchantConfigResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(PaymentMerchantPermissions.Update));

        group.MapPost("/{merchantConfigId:guid}/disable", async (
            Guid merchantConfigId,
            PaymentMerchantConfigManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(merchantConfigId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("paymentsDisableMerchantConfig")
        .Produces<PaymentMerchantConfigResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(PaymentMerchantPermissions.Update));
    }
}
