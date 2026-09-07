using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Security;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>支付宝电脑网站支付（Page Pay）OpenAPI 客户端。</summary>
/// <param name="httpClientFactory">创建受控超时的支付提供程序客户端。</param>
/// <param name="secretProtector">解密仅用于当前外部调用的商户凭据。</param>
internal sealed partial class AlipayPagePayClient(
    IHttpClientFactory httpClientFactory,
    PaymentSecretProtector secretProtector)
{
    /// <summary>HttpClient 注册名称。</summary>
    public const string HttpClientName = "Full.NET.Payments.AlipayPage";

    private const string GatewayUrl = "https://openapi.alipay.com/gateway.do";
    private const string PagePayMethod = "alipay.trade.page.pay";
    private const string QueryMethod = "alipay.trade.query";
    private const string ProductCode = "FAST_INSTANT_TRADE_PAY";

    /// <summary>支付提供程序请求和响应的闭合 JSON 元数据。</summary>
    [JsonSerializable(typeof(AlipayGatewayResponse))]
    [JsonSerializable(typeof(AlipayPagePayBizContent))]
    [JsonSerializable(typeof(AlipayTradeQueryBizContent))]
    private partial class AlipayProviderJsonContext : JsonSerializerContext;

    private static readonly JsonSerializerOptions AlipayJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly AlipayProviderJsonContext ProviderJson = new(AlipayJsonOptions);

    /// <summary>构建 Page Pay 跳转 URL。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="amountMinor">订单金额（分）。</param>
    /// <param name="currency">货币代码；当前仅支持 CNY。</param>
    /// <param name="subject">商品标题。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回跳转 URL；失败时返回错误摘要。</returns>
    public Task<AlipayPagePayResult> CreatePagePayUrlAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        long amountMinor,
        string currency,
        string subject,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(merchantConfig.PrivateKeyProtected))
        {
            return Task.FromResult(AlipayPagePayResult.Failure("Merchant secrets are not configured."));
        }

        if (string.IsNullOrWhiteSpace(merchantConfig.ReturnUrl))
        {
            return Task.FromResult(AlipayPagePayResult.Failure("Return URL is not configured for Alipay Page Pay."));
        }

        if (!string.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AlipayPagePayResult.Failure("Alipay Page Pay currently supports CNY only."));
        }

        var bizContent = JsonSerializer.Serialize(
            new AlipayPagePayBizContent(
                outTradeNo,
                FormatAmountYuan(amountMinor),
                subject.Trim(),
                ProductCode),
            ProviderJson.AlipayPagePayBizContent);

        var parameters = BuildCommonParameters(merchantConfig, PagePayMethod, bizContent);
        parameters["notify_url"] = merchantConfig.NotifyUrl.Trim();
        parameters["return_url"] = merchantConfig.ReturnUrl.Trim();

        var payUrl = BuildSignedGatewayUrl(merchantConfig, parameters);
        return Task.FromResult(AlipayPagePayResult.Success(payUrl));
    }

    /// <summary>按商户订单号查询支付宝交易状态。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询结果。</returns>
    public async Task<AlipayTradeQueryResult> QueryTradeByOutTradeNoAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(merchantConfig.PrivateKeyProtected))
        {
            return AlipayTradeQueryResult.Failure("Merchant secrets are not configured.");
        }

        var bizContent = JsonSerializer.Serialize(
            new AlipayTradeQueryBizContent(outTradeNo),
            ProviderJson.AlipayTradeQueryBizContent);
        var parameters = BuildCommonParameters(merchantConfig, QueryMethod, bizContent);
        var requestUrl = BuildSignedGatewayUrl(merchantConfig, parameters);

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return AlipayTradeQueryResult.Failure(
                $"Alipay trade query failed with status {(int)response.StatusCode}: {responseBody}");
        }

        var parsed = JsonSerializer.Deserialize(responseBody, ProviderJson.AlipayGatewayResponse);
        var queryResponse = parsed?.AlipayTradeQueryResponse;
        if (queryResponse is null)
        {
            return AlipayTradeQueryResult.Failure("Alipay trade query response was empty.");
        }

        if (!string.Equals(queryResponse.Code, "10000", StringComparison.Ordinal))
        {
            return AlipayTradeQueryResult.Failure(
                queryResponse.SubMsg ?? queryResponse.Msg ?? "Alipay trade query failed.");
        }

        if (string.IsNullOrWhiteSpace(queryResponse.TradeStatus))
        {
            return AlipayTradeQueryResult.Failure("Alipay trade query response did not include trade_status.");
        }

        var amountMinor = ParseAmountMinor(queryResponse.TotalAmount);
        if (amountMinor is null)
        {
            return AlipayTradeQueryResult.Failure("Alipay trade query response did not include a valid total_amount.");
        }

        return AlipayTradeQueryResult.Success(
            queryResponse.TradeStatus,
            queryResponse.TradeNo,
            amountMinor.Value);
    }

    private Dictionary<string, string> BuildCommonParameters(
        PaymentMerchantConfigRecord merchantConfig,
        string method,
        string bizContent) =>
        new(StringComparer.Ordinal)
        {
            ["app_id"] = merchantConfig.AppId.Trim(),
            ["method"] = method,
            ["format"] = "JSON",
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2",
            ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["version"] = "1.0",
            ["biz_content"] = bizContent,
        };

    private string BuildSignedGatewayUrl(
        PaymentMerchantConfigRecord merchantConfig,
        Dictionary<string, string> parameters)
    {
        var signContent = AlipaySigner.BuildSignContent(parameters);
        var privateKeyPem = secretProtector.UnprotectPrivateKey(merchantConfig.PrivateKeyProtected!);
        parameters["sign"] = AlipaySigner.Sign(signContent, privateKeyPem);
        return $"{GatewayUrl}?{BuildQueryString(parameters)}";
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new StringBuilder();
        foreach (var pair in parameters.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value));
        }

        return builder.ToString();
    }

    private static string FormatAmountYuan(long amountMinor) =>
        (amountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static long? ParseAmountMinor(string? totalAmount)
    {
        if (!decimal.TryParse(
                totalAmount,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var yuan))
        {
            return null;
        }

        return (long)Math.Round(yuan * 100m, MidpointRounding.AwayFromZero);
    }

    private sealed record AlipayPagePayBizContent(
        [property: JsonPropertyName("out_trade_no")] string OutTradeNo,
        [property: JsonPropertyName("total_amount")] string TotalAmount,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("product_code")] string ProductCode);

    private sealed record AlipayTradeQueryBizContent(
        [property: JsonPropertyName("out_trade_no")] string OutTradeNo);

    private sealed record AlipayGatewayResponse(
        [property: JsonPropertyName("alipay_trade_query_response")]
        AlipayTradeQueryResponseBody? AlipayTradeQueryResponse);

    private sealed record AlipayTradeQueryResponseBody(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("msg")] string? Msg,
        [property: JsonPropertyName("sub_msg")] string? SubMsg,
        [property: JsonPropertyName("trade_status")] string? TradeStatus,
        [property: JsonPropertyName("trade_no")] string? TradeNo,
        [property: JsonPropertyName("total_amount")] string? TotalAmount);
}

/// <summary>支付宝 Page Pay 下单结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="PayUrl">跳转 URL。</param>
/// <param name="FailMessage">失败摘要。</param>
internal sealed record AlipayPagePayResult(bool Succeeded, string? PayUrl, string? FailMessage)
{
    /// <summary>构建成功结果。</summary>
    /// <param name="payUrl">跳转 URL。</param>
    /// <returns>成功结果。</returns>
    public static AlipayPagePayResult Success(string payUrl) =>
        new(true, payUrl, null);

    /// <summary>构建失败结果。</summary>
    /// <param name="failMessage">失败摘要。</param>
    /// <returns>失败结果。</returns>
    public static AlipayPagePayResult Failure(string failMessage) =>
        new(false, null, failMessage);
}

/// <summary>支付宝交易查询结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="TradeStatus">渠道交易状态。</param>
/// <param name="TradeNo">支付宝交易号。</param>
/// <param name="AmountMinor">订单金额（分）。</param>
/// <param name="FailMessage">失败摘要。</param>
internal sealed record AlipayTradeQueryResult(
    bool Succeeded,
    string? TradeStatus,
    string? TradeNo,
    long? AmountMinor,
    string? FailMessage)
{
    /// <summary>构建成功结果。</summary>
    /// <param name="tradeStatus">渠道交易状态。</param>
    /// <param name="tradeNo">支付宝交易号。</param>
    /// <param name="amountMinor">订单金额（分）。</param>
    /// <returns>成功结果。</returns>
    public static AlipayTradeQueryResult Success(
        string tradeStatus,
        string? tradeNo,
        long amountMinor) =>
        new(true, tradeStatus, tradeNo, amountMinor, null);

    /// <summary>构建失败结果。</summary>
    /// <param name="failMessage">失败摘要。</param>
    /// <returns>失败结果。</returns>
    public static AlipayTradeQueryResult Failure(string failMessage) =>
        new(false, null, null, null, failMessage);
}
