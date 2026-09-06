using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>支付退款分页列表与详情只读查询。</summary>
internal sealed class PaymentRefundQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取退款详情。</summary>
    /// <param name="refundId">退款标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或稳定业务错误。</returns>
    public async Task<Result<PaymentRefundResponse>> GetByIdAsync(
        Guid refundId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<PaymentRefundRecord>(
                PaymentRefundSql.FindById,
                PaymentSqlParameters.Create(("RefundId", refundId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFoundDetail();
        }

        return Result<PaymentRefundResponse>.Success(PaymentRefundMapper.MapDetail(row));
    }

    /// <summary>分页查询退款列表。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选租户筛选。</param>
    /// <param name="orderId">可选订单筛选。</param>
    /// <param name="refundStateKey">可选退款状态筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<PaymentRefundListItem>>> ListAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        Guid? orderId,
        string? refundStateKey,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = PaymentSqlParameters.Create(
            ("TenantId", tenantId),
            ("OrderId", orderId),
            ("RefundStateKey", NormalizeFilter(refundStateKey)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                PaymentRefundSql.CountSqlServer,
                PaymentRefundSql.ListSqlServer),
            DatabaseProvider.MySql => (
                PaymentRefundSql.CountMySql,
                PaymentRefundSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("payments.count_refunds", countStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<PaymentRefundRecord>(
                new SqlStatement("payments.list_refunds", listStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<PaymentRefundListItem>>.Success(
            new PagedResult<PaymentRefundListItem>(
                rows.Select(PaymentRefundMapper.MapListItem).ToArray(),
                page,
                pageSize,
                total));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<PaymentRefundResponse> NotFoundDetail() =>
        Result<PaymentRefundResponse>.Failure(new Error(
            PaymentErrorCodes.RefundNotFound,
            "The payment refund was not found.",
            ErrorType.NotFound));
}
