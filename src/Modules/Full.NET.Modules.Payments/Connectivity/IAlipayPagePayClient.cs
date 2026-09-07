using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>支付宝 Page Pay 的受控远程边界，禁止在数据库事务内调用。</summary>
internal interface IAlipayPagePayClient
{
    /// <summary>构建 Page Pay 跳转 URL。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">已持久化的商户订单号。</param>
    /// <param name="amountMinor">订单金额（分）。</param>
    /// <param name="currency">货币代码。</param>
    /// <param name="subject">商品标题。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回跳转 URL；失败时返回错误摘要。</returns>
    Task<AlipayPagePayResult> CreatePagePayUrlAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        long amountMinor,
        string currency,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>按商户订单号查询支付宝交易状态。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询结果。</returns>
    Task<AlipayTradeQueryResult> QueryTradeByOutTradeNoAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        CancellationToken cancellationToken = default);
}
