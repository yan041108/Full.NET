using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>微信 Native 支付与退款的受控远程边界，禁止在数据库事务内调用。</summary>
internal interface IWeChatNativePayClient
{
    /// <summary>调用微信 Native 下单并返回二维码链接。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">已持久化的商户订单号，作为渠道幂等键。</param>
    /// <param name="amountMinor">订单金额（分）。</param>
    /// <param name="currency">货币代码。</param>
    /// <param name="description">商品描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回 code_url；明确失败时返回错误摘要。</returns>
    Task<WeChatNativePayResult> CreateNativeOrderAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        long amountMinor,
        string currency,
        string description,
        CancellationToken cancellationToken = default);

    /// <summary>按商户订单号查询微信交易状态，用于未知结果对账。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询结果。</returns>
    Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        CancellationToken cancellationToken = default);

    /// <summary>调用微信国内退款；相同退款单号必须保持幂等。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="outRefundNo">已持久化的商户退款单号。</param>
    /// <param name="amountMinor">退款金额（分）。</param>
    /// <param name="totalMinor">原订单金额（分）。</param>
    /// <param name="currency">货币代码。</param>
    /// <param name="reason">退款原因。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退款结果。</returns>
    Task<WeChatRefundResult> CreateDomesticRefundAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        string outRefundNo,
        long amountMinor,
        long totalMinor,
        string currency,
        string reason,
        CancellationToken cancellationToken = default);
}
