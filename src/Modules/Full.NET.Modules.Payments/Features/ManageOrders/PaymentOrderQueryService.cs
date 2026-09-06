using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Payments.Features.ManageOrders;

/// <summary>支付订单分页列表与详情只读查询。</summary>
internal sealed class PaymentOrderQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取支付订单详情。</summary>
    /// <param name="orderId">订单标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或稳定业务错误。</returns>
    public async Task<Result<PaymentOrderResponse>> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<PaymentOrderRecord>(
                PaymentOrderSql.FindById,
                PaymentSqlParameters.Create(("OrderId", orderId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFoundDetail();
        }

        return Result<PaymentOrderResponse>.Success(PaymentOrderMapper.MapDetail(row));
    }

    /// <summary>分页查询支付订单列表。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选租户筛选。</param>
    /// <param name="tradeStateKey">可选交易状态筛选。</param>
    /// <param name="outTradeNoContains">可选商户订单号模糊筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<PaymentOrderListItem>>> ListAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        string? tradeStateKey,
        string? outTradeNoContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = PaymentSqlParameters.Create(
            ("TenantId", tenantId),
            ("TradeStateKey", NormalizeFilter(tradeStateKey)),
            ("OutTradeNoContains", NormalizeFilter(outTradeNoContains)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                PaymentOrderSql.CountSqlServer,
                PaymentOrderSql.ListSqlServer),
            DatabaseProvider.MySql => (
                PaymentOrderSql.CountMySql,
                PaymentOrderSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("payments.count_orders", countStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<PaymentOrderRecord>(
                new SqlStatement("payments.list_orders", listStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<PaymentOrderListItem>>.Success(
            new PagedResult<PaymentOrderListItem>(
                rows.Select(PaymentOrderMapper.MapListItem).ToArray(),
                page,
                pageSize,
                total));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<PaymentOrderResponse> NotFoundDetail() =>
        Result<PaymentOrderResponse>.Failure(new Error(
            PaymentErrorCodes.OrderNotFound,
            "The payment order was not found.",
            ErrorType.NotFound));
}
