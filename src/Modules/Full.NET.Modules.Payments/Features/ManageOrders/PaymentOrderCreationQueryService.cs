using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageOrders;

/// <summary>Host 按租户与原创建身份读取本地意图，恢复核对不要求租户仍可创建新订单。</summary>
internal sealed class PaymentOrderCreationQueryService(IQueryExecutor queries, ICurrentTenant currentTenant, IClock clock)
{
    internal async Task<Result<PaymentOrderCreationLookupResponse>> GetAsync(Guid tenantId, Guid idempotencyKey, CancellationToken token)
    {
        if (!currentTenant.IsHost)
            return Result<PaymentOrderCreationLookupResponse>.Failure(new Error(PaymentErrorCodes.OrderCreationLookupForbidden,
                "Order creation requests can be read only in the Host context.", ErrorType.Forbidden));
        if (tenantId == Guid.Empty || idempotencyKey == Guid.Empty) return NotFound();
        var row = await queries.QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindByIdempotencyKey,
            PaymentSqlParameters.Create(("TenantId", tenantId), ("IdempotencyKey", idempotencyKey)), token).ConfigureAwait(false);
        if (row is null || row.Id == Guid.Empty || row.TenantId != tenantId || row.IdempotencyKey is not Guid storedKey || storedKey != idempotencyKey) return NotFound();
        return Result<PaymentOrderCreationLookupResponse>.Success(new(storedKey, PaymentOrderMapper.MapDetail(row), clock.UtcNow));
    }

    private static Result<PaymentOrderCreationLookupResponse> NotFound() => Result<PaymentOrderCreationLookupResponse>.Failure(new Error(
        PaymentErrorCodes.OrderCreationRequestNotFound, "No order was found for the tenant and original creation request.", ErrorType.NotFound));
}
