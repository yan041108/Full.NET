using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Payments.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>支付退款查询与创建 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册退款管理路由。</summary>
    /// <param name="endpoints">路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/payments/refunds")
            .WithTags("PaymentRefunds");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? tenantId,
            Guid? orderId,
            string? refundStateKey,
            PaymentRefundQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    tenantId,
                    orderId,
                    refundStateKey,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("paymentsListRefunds")
        .Produces<PagedResult<PaymentRefundListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(PaymentRefundPermissions.Read));

        group.MapGet("/{refundId:guid}", async (
            Guid refundId,
            PaymentRefundQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(refundId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("paymentsGetRefund")
        .Produces<PaymentRefundResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(PaymentRefundPermissions.Read));
    }
}
