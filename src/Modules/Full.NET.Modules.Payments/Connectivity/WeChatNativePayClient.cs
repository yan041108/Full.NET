using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Security;

namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>微信 Native 支付 API v3 客户端。</summary>
/// <param name="httpClientFactory">创建受控超时的支付提供程序客户端。</param>
/// <param name="secretProtector">解密仅用于当前外部调用的商户凭据。</param>
internal sealed partial class WeChatNativePayClient(
    IHttpClientFactory httpClientFactory,
    PaymentSecretProtector secretProtector) : IWeChatNativePayClient
{
    /// <summary>HttpClient 注册名称。</summary>
    public const string HttpClientName = "Full.NET.Payments.WeChatNative";

    private const string NativePayPath = "/v3/pay/transactions/native";
    private const string CertificatesPath = "/v3/certificates";
    private const string DomesticRefundPath = "/v3/refund/domestic/refunds";

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
            ProviderJson.WeChatNativePayRequest);

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

        var parsed = JsonSerializer.Deserialize(responseBody, ProviderJson.WeChatNativePayResponse);
        if (parsed?.CodeUrl is null or { Length: 0 })
        {
            return WeChatNativePayResult.Failure("WeChat Native pay response did not include code_url.");
        }

        return WeChatNativePayResult.Success(parsed.CodeUrl);
    }

    /// <summary>按商户订单号查询微信交易状态。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询结果。</returns>
    public async Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(merchantConfig.ApiV3KeyProtected)
            || string.IsNullOrWhiteSpace(merchantConfig.PrivateKeyProtected))
        {
            return WeChatTransactionQueryResult.Failure("Merchant secrets are not configured.");
        }

        var path = $"/v3/pay/transactions/out-trade-no/{Uri.EscapeDataString(outTradeNo)}?mchid={Uri.EscapeDataString(merchantConfig.MerchantId)}";
        var responseBody = await SendSignedRequestAsync(
                merchantConfig,
                HttpMethod.Get,
                path,
                string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        if (responseBody.IsFailure)
        {
            return WeChatTransactionQueryResult.Failure(responseBody.FailMessage!);
        }

        var parsed = JsonSerializer.Deserialize(responseBody.Body!, ProviderJson.WeChatTransactionQueryResponse);
        if (parsed is null)
        {
            return WeChatTransactionQueryResult.Failure("WeChat transaction query response was invalid.");
        }

        return WeChatTransactionQueryResult.Success(
            parsed.TradeState ?? string.Empty,
            parsed.TransactionId,
            parsed.Amount?.Total ?? 0);
    }

    /// <summary>发起微信国内退款。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="outRefundNo">商户退款单号。</param>
    /// <param name="amountMinor">退款金额（分）。</param>
    /// <param name="totalMinor">原订单金额（分）。</param>
    /// <param name="currency">货币代码。</param>
    /// <param name="reason">退款原因。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退款结果。</returns>
    public async Task<WeChatRefundResult> CreateDomesticRefundAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        string outRefundNo,
        long amountMinor,
        long totalMinor,
        string currency,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(merchantConfig.ApiV3KeyProtected)
            || string.IsNullOrWhiteSpace(merchantConfig.PrivateKeyProtected))
        {
            return WeChatRefundResult.Failure("Merchant secrets are not configured.");
        }

        var requestBody = JsonSerializer.Serialize(
            new WeChatDomesticRefundRequest(
                outTradeNo,
                outRefundNo,
                reason,
                new WeChatRefundAmount(amountMinor, totalMinor, currency)),
            ProviderJson.WeChatDomesticRefundRequest);

        var responseBody = await SendSignedRequestAsync(
                merchantConfig,
                HttpMethod.Post,
                DomesticRefundPath,
                requestBody,
                cancellationToken)
            .ConfigureAwait(false);
        if (responseBody.IsFailure)
        {
            return WeChatRefundResult.Failure(responseBody.FailMessage!);
        }

        var parsed = JsonSerializer.Deserialize(responseBody.Body!, ProviderJson.WeChatDomesticRefundResponse);
        if (parsed is null)
        {
            return WeChatRefundResult.Failure("WeChat refund response was invalid.");
        }

        return WeChatRefundResult.Success(
            parsed.RefundId,
            parsed.Status ?? PaymentRefundStateKeys.Processing);
    }

    /// <summary>拉取并解密微信支付平台证书公钥 PEM。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="platformSerialNo">平台证书序列号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>公钥 PEM；无法解析时返回 null。</returns>
    public async Task<string?> FetchPlatformPublicKeyPemAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string platformSerialNo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(merchantConfig.ApiV3KeyProtected)
            || string.IsNullOrWhiteSpace(merchantConfig.PrivateKeyProtected))
        {
            return null;
        }

        var responseBody = await SendSignedRequestAsync(
                merchantConfig,
                HttpMethod.Get,
                CertificatesPath,
                string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        if (responseBody.IsFailure || string.IsNullOrWhiteSpace(responseBody.Body))
        {
            return null;
        }

        var parsed = JsonSerializer.Deserialize(responseBody.Body, ProviderJson.WeChatCertificatesResponse);
        var apiV3Key = secretProtector.UnprotectApiV3Key(merchantConfig.ApiV3KeyProtected);
        foreach (var item in parsed?.Data ?? [])
        {
            if (!string.Equals(item.SerialNo, platformSerialNo, StringComparison.OrdinalIgnoreCase)
                || item.EncryptCertificate is null)
            {
                continue;
            }

            var pem = WeChatPayResourceDecryptor.Decrypt(
                apiV3Key,
                item.EncryptCertificate.AssociatedData ?? string.Empty,
                item.EncryptCertificate.Nonce ?? string.Empty,
                item.EncryptCertificate.Ciphertext ?? string.Empty);
            return ExtractPublicKeyPemFromCertificate(pem);
        }

        return null;
    }

    private async Task<WeChatSignedResponse> SendSignedRequestAsync(
        PaymentMerchantConfigRecord merchantConfig,
        HttpMethod method,
        string path,
        string requestBody,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Guid.NewGuid().ToString("N");
        var privateKeyPem = secretProtector.UnprotectPrivateKey(merchantConfig.PrivateKeyProtected!);
        var canonicalMessage = WeChatPaySigner.BuildCanonicalMessage(
            method.Method,
            path,
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

        using var request = new HttpRequestMessage(method, path);
        if (requestBody.Length > 0)
        {
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        }

        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return WeChatSignedResponse.Failure(
                $"WeChat request failed with status {(int)response.StatusCode}: {body}");
        }

        return WeChatSignedResponse.Success(body);
    }

    private static string ExtractPublicKeyPemFromCertificate(string certificatePem)
    {
        var cert = X509Certificate2.CreateFromPem(certificatePem);
        return cert.GetRSAPublicKey()?.ExportSubjectPublicKeyInfoPem()
            ?? throw new InvalidOperationException("WeChat platform certificate does not contain an RSA public key.");
    }

    /// <summary>支付提供程序请求和响应的闭合 JSON 元数据。</summary>
    [JsonSerializable(typeof(WeChatCertificatesResponse))]
    [JsonSerializable(typeof(WeChatDomesticRefundRequest))]
    [JsonSerializable(typeof(WeChatDomesticRefundResponse))]
    [JsonSerializable(typeof(WeChatNativePayRequest))]
    [JsonSerializable(typeof(WeChatNativePayResponse))]
    [JsonSerializable(typeof(WeChatTransactionQueryResponse))]
    private partial class WeChatProviderJsonContext : JsonSerializerContext;

    private static readonly JsonSerializerOptions WeChatJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly WeChatProviderJsonContext ProviderJson = new(WeChatJsonOptions);

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

    private sealed record WeChatTransactionQueryResponse(
        [property: JsonPropertyName("trade_state")] string? TradeState,
        [property: JsonPropertyName("transaction_id")] string? TransactionId,
        [property: JsonPropertyName("amount")] WeChatNativePayAmount? Amount);

    private sealed record WeChatDomesticRefundRequest(
        [property: JsonPropertyName("out_trade_no")] string OutTradeNo,
        [property: JsonPropertyName("out_refund_no")] string OutRefundNo,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("amount")] WeChatRefundAmount Amount);

    private sealed record WeChatRefundAmount(
        [property: JsonPropertyName("refund")] long Refund,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("currency")] string Currency);

    private sealed record WeChatDomesticRefundResponse(
        [property: JsonPropertyName("refund_id")] string? RefundId,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record WeChatCertificatesResponse(
        [property: JsonPropertyName("data")] WeChatCertificateItem[]? Data);

    private sealed record WeChatCertificateItem(
        [property: JsonPropertyName("serial_no")] string? SerialNo,
        [property: JsonPropertyName("encrypt_certificate")] WeChatEncryptedCertificate? EncryptCertificate);

    private sealed record WeChatEncryptedCertificate(
        [property: JsonPropertyName("algorithm")] string? Algorithm,
        [property: JsonPropertyName("nonce")] string? Nonce,
        [property: JsonPropertyName("associated_data")] string? AssociatedData,
        [property: JsonPropertyName("ciphertext")] string? Ciphertext);
}

/// <summary>微信签名请求结果。</summary>
internal sealed record WeChatSignedResponse(bool IsFailure, string? Body, string? FailMessage)
{
    /// <summary>创建成功结果。</summary>
    public static WeChatSignedResponse Success(string body) => new(false, body, null);

    /// <summary>创建失败结果。</summary>
    public static WeChatSignedResponse Failure(string failMessage) => new(true, null, failMessage);
}

/// <summary>微信交易查询结果。</summary>
internal sealed record WeChatTransactionQueryResult(
    bool Succeeded,
    string? TradeState,
    string? TransactionId,
    long AmountMinor,
    string? FailMessage)
{
    public static WeChatTransactionQueryResult Success(
        string tradeState,
        string? transactionId,
        long amountMinor) =>
        new(true, tradeState, transactionId, amountMinor, null);

    public static WeChatTransactionQueryResult Failure(string failMessage) =>
        new(false, null, null, 0, failMessage);
}

/// <summary>微信退款结果。</summary>
internal sealed record WeChatRefundResult(
    bool Succeeded,
    string? ProviderRefundId,
    string? StatusKey,
    string? FailMessage)
{
    public static WeChatRefundResult Success(string? providerRefundId, string statusKey) =>
        new(true, providerRefundId, statusKey, null);

    public static WeChatRefundResult Failure(string failMessage) =>
        new(false, null, null, failMessage);
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
