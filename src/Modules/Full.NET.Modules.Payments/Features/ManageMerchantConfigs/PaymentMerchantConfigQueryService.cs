using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Payments.Features.ManageMerchantConfigs;

/// <summary>支付商户配置分页列表与详情只读查询。</summary>
internal sealed class PaymentMerchantConfigQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取商户配置详情。</summary>
    /// <param name="merchantConfigId">配置标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或稳定业务错误。</returns>
    public async Task<Result<PaymentMerchantConfigResponse>> GetByIdAsync(
        Guid merchantConfigId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                PaymentMerchantConfigSql.FindById,
                PaymentSqlParameters.Create(("MerchantConfigId", merchantConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFoundDetail();
        }

        return Result<PaymentMerchantConfigResponse>.Success(PaymentMerchantConfigMapper.MapDetail(row));
    }

    /// <summary>分页查询商户配置列表（脱敏）。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选租户筛选。</param>
    /// <param name="channelKey">可选渠道筛选。</param>
    /// <param name="nameContains">可选名称模糊筛选。</param>
    /// <param name="isEnabled">可选启用状态筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<PaymentMerchantConfigListItem>>> ListAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        string? channelKey,
        string? nameContains,
        bool? isEnabled,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = PaymentSqlParameters.Create(
            ("TenantId", tenantId),
            ("ChannelKey", NormalizeFilter(channelKey)),
            ("NameContains", NormalizeFilter(nameContains)),
            ("IsEnabled", isEnabled),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                PaymentMerchantConfigSql.CountSqlServer,
                PaymentMerchantConfigSql.ListSqlServer),
            DatabaseProvider.MySql => (
                PaymentMerchantConfigSql.CountMySql,
                PaymentMerchantConfigSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("payments.count_merchant_configs", countStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<PaymentMerchantConfigRecord>(
                new SqlStatement("payments.list_merchant_configs", listStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<PaymentMerchantConfigListItem>>.Success(
            new PagedResult<PaymentMerchantConfigListItem>(
                rows.Select(PaymentMerchantConfigMapper.MapListItem).ToArray(),
                page,
                pageSize,
                total));
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<PaymentMerchantConfigResponse> NotFoundDetail() =>
        Result<PaymentMerchantConfigResponse>.Failure(new Error(
            PaymentErrorCodes.MerchantConfigNotFound,
            "The payment merchant configuration was not found.",
            ErrorType.NotFound));
}
