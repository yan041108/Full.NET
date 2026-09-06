using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Security;

namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>微信 Native 支付 API v3 客户端。</summary>
internal sealed class WeChatNativePayClient(
    IHttpClientFactory httpClientFactory,
    PaymentSecretProtector secretProtector)
{
    /// <summary>HttpClient 注册名称。</summary>
    public const string HttpClientName = "Full.NET.Payments.WeChatNative";

    private const string NativePayPath = "/v3/pay/transactions/native";

    /// <summary>调用微信 Native 下单并返回二维码链接。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="amountMinor">订单金额（分）。</param>
    /// <param name="currency">货币代码。</param>
    /// <param name="description">商品描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回 code_url；失败时返回错误摘要。</returns>
    public async Task<WeChatNativePayResult> CreateNativeOrderAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        long amountMinor,
        string currency,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(merchantConfig.ApiV3KeyProtected)
            || string.IsNullOrWhiteSpace(merchantConfig.PrivateKeyProtected))
        {
            return WeChatNativePayResult.Failure("Merchant secrets are not configured.");
        }

        var requestBody = JsonSerializer.Serialize(
            new WeChatNativePayRequest(
                merchantConfig.AppId,
                merchantConfig.MerchantId,
                description,
                outTradeNo,
                merchantConfig.NotifyUrl,
                new WeChatNativePayAmount(amountMinor, currency)),
            WeChatJsonOptions);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Guid.NewGuid().ToString("N");
        var privateKeyPem = secretProtector.UnprotectPrivateKey(merchantConfig.PrivateKeyProtected);
        var canonicalMessage = WeChatPaySigner.BuildCanonicalMessage(
            "POST",
            NativePayPath,
            timestamp,
            nonce,
            requestBody);
        var signature = WeChatPaySigner.Sign(canonicalMessage, privateKeyPem);
        var authorization = WeChatPaySigner.BuildAuthorizationHeader(
            merchantConfig.MerchantId,
            merchantConfig.CertificateSerialNo,
            timestamp,
            nonce,
            signature);

        using var request = new HttpRequestMessage(HttpMethod.Post, NativePayPath)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return WeChatNativePayResult.Failure(
                $"WeChat Native pay request failed with status {(int)response.StatusCode}: {responseBody}");
        }

        var parsed = JsonSerializer.Deserialize<WeChatNativePayResponse>(responseBody, WeChatJsonOptions);
        if (parsed?.CodeUrl is null or { Length: 0 })
        {
            return WeChatNativePayResult.Failure("WeChat Native pay response did not include code_url.");
        }

        return WeChatNativePayResult.Success(parsed.CodeUrl);
    }

    private static readonly JsonSerializerOptions WeChatJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>微信 Native 下单请求体。</summary>
    private sealed record WeChatNativePayRequest(
        [property: JsonPropertyName("appid")] string AppId,
        [property: JsonPropertyName("mchid")] string MchId,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("out_trade_no")] string OutTradeNo,
        [property: JsonPropertyName("notify_url")] string NotifyUrl,
        [property: JsonPropertyName("amount")] WeChatNativePayAmount Amount);

    /// <summary>微信 Native 金额信息。</summary>
    private sealed record WeChatNativePayAmount(
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("currency")] string Currency);

    /// <summary>微信 Native 下单成功响应。</summary>
    private sealed record WeChatNativePayResponse(
        [property: JsonPropertyName("code_url")] string? CodeUrl);
}

/// <summary>微信 Native 下单结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="CodeUrl">二维码链接。</param>
/// <param name="FailMessage">失败摘要。</param>
internal sealed record WeChatNativePayResult(bool Succeeded, string? CodeUrl, string? FailMessage)
{
    /// <summary>创建成功结果。</summary>
    /// <param name="codeUrl">二维码链接。</param>
    /// <returns>成功结果。</returns>
    public static WeChatNativePayResult Success(string codeUrl) =>
        new(true, codeUrl, null);

    /// <summary>创建失败结果。</summary>
    /// <param name="failMessage">失败摘要。</param>
    /// <returns>失败结果。</returns>
    public static WeChatNativePayResult Failure(string failMessage) =>
        new(false, null, failMessage);
}
