using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;

/// <summary>通过微信开放平台调用 token、jscode2session 与 subscribe/send。</summary>
internal sealed partial class HttpWeChatMiniProgramTransport(IHttpClientFactory httpClientFactory)
    : IWeChatMiniProgramTransport
{
    public const string HttpClientName = "Notifications.WeChatMiniProgram";

    private const string TokenEndpoint = "https://api.weixin.qq.com/cgi-bin/token";
    private const string JsCodeEndpoint = "https://api.weixin.qq.com/sns/jscode2session";
    private const string SubscribeSendEndpoint = "https://api.weixin.qq.com/cgi-bin/message/subscribe/send";

    public async ValueTask<WeChatMiniProgramAccessToken> GetAccessTokenAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url =
            $"{TokenEndpoint}?grant_type=client_credential&appid={Uri.EscapeDataString(appId)}&secret={Uri.EscapeDataString(appSecret)}";
        var body = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseAccessToken(body);
    }

    public async ValueTask<WeChatMiniProgramSession> ExchangeJsCodeAsync(
        string appId,
        string appSecret,
        string jsCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url =
            $"{JsCodeEndpoint}?appid={Uri.EscapeDataString(appId)}&secret={Uri.EscapeDataString(appSecret)}&js_code={Uri.EscapeDataString(jsCode)}&grant_type=authorization_code";
        var body = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseSession(body);
    }

    public async ValueTask<string> SendSubscribeMessageAsync(
        string accessToken,
        WeChatMiniProgramSubscribeSendCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dataDocument = JsonDocument.Parse(command.DataJson);
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["touser"] = command.ToUserOpenId,
            ["template_id"] = command.TemplateId,
            ["page"] = command.Page,
            ["data"] = dataDocument.RootElement.Clone(),
            ["miniprogram_state"] = "formal",
            ["lang"] = "zh_CN",
        });
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SubscribeSendEndpoint}?access_token={Uri.EscapeDataString(accessToken)}")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new WeChatMiniProgramTransportException(
                WeChatMiniProgramTransportFailureKind.Transient,
                "The WeChat subscribe endpoint could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeChatMiniProgramTransportException(
                    ClassifyHttpStatus(response.StatusCode),
                    "The WeChat subscribe endpoint returned an error response.");
            }

            return ParseMsgId(body);
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new WeChatMiniProgramTransportException(
                WeChatMiniProgramTransportFailureKind.Transient,
                "The WeChat endpoint could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeChatMiniProgramTransportException(
                    ClassifyHttpStatus(response.StatusCode),
                    "The WeChat endpoint returned an error response.");
            }

            return body;
        }
    }

    private static WeChatMiniProgramAccessToken ParseAccessToken(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errCode = root.TryGetProperty("errcode", out var errCodeElement)
                ? errCodeElement.GetInt32()
                : 0;
            if (errCode != 0)
            {
                throw new WeChatMiniProgramTransportException(
                    ClassifyApiError(errCode),
                    "The WeChat token endpoint rejected the request.");
            }

            if (!root.TryGetProperty("access_token", out var tokenElement)
                || tokenElement.GetString() is not { Length: > 0 } token)
            {
                throw new WeChatMiniProgramTransportException(
                    WeChatMiniProgramTransportFailureKind.Transient,
                    "The WeChat token response did not include an access token.");
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt32()
                : 7200;
            if (expiresIn < 60)
            {
                expiresIn = 60;
            }

            return new WeChatMiniProgramAccessToken(token, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }
        catch (JsonException)
        {
            throw new WeChatMiniProgramTransportException(
                WeChatMiniProgramTransportFailureKind.Transient,
                "The WeChat token response was invalid.");
        }
    }

    private static WeChatMiniProgramSession ParseSession(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errCode = root.TryGetProperty("errcode", out var errCodeElement)
                ? errCodeElement.GetInt32()
                : 0;
            if (errCode != 0)
            {
                throw new WeChatMiniProgramTransportException(
                    ClassifyApiError(errCode),
                    "The WeChat jscode2session endpoint rejected the request.");
            }

            if (!root.TryGetProperty("openid", out var openIdElement)
                || openIdElement.GetString() is not { Length: > 0 } openId
                || !WeChatMiniProgramNotificationProviderAdapter.IsValidOpenId(openId))
            {
                throw new WeChatMiniProgramTransportException(
                    WeChatMiniProgramTransportFailureKind.Permanent,
                    "The WeChat session response did not include a valid OpenId.");
            }

            string? unionId = null;
            if (root.TryGetProperty("unionid", out var unionIdElement)
                && unionIdElement.GetString() is { Length: > 0 } parsedUnionId)
            {
                unionId = parsedUnionId;
            }

            return new WeChatMiniProgramSession(openId, unionId);
        }
        catch (JsonException)
        {
            throw new WeChatMiniProgramTransportException(
                WeChatMiniProgramTransportFailureKind.Transient,
                "The WeChat session response was invalid.");
        }
    }

    private static string ParseMsgId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errCode = root.TryGetProperty("errcode", out var errCodeElement)
                ? errCodeElement.GetInt32()
                : -1;
            if (errCode != 0)
            {
                throw new WeChatMiniProgramTransportException(
                    ClassifyApiError(errCode),
                    "The WeChat subscribe endpoint rejected the request.");
            }

            if (root.TryGetProperty("msgid", out var msgIdElement)
                && (msgIdElement.ValueKind == JsonValueKind.String
                    ? msgIdElement.GetString()
                    : msgIdElement.GetRawText()) is { Length: > 0 } msgId)
            {
                return msgId;
            }

            return Guid.NewGuid().ToString("N");
        }
        catch (JsonException)
        {
            throw new WeChatMiniProgramTransportException(
                WeChatMiniProgramTransportFailureKind.Transient,
                "The WeChat subscribe response was invalid.");
        }
    }

    private static WeChatMiniProgramTransportFailureKind ClassifyHttpStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests => WeChatMiniProgramTransportFailureKind.RateLimited,
            >= System.Net.HttpStatusCode.InternalServerError => WeChatMiniProgramTransportFailureKind.Transient,
            _ => WeChatMiniProgramTransportFailureKind.Permanent,
        };

    private static WeChatMiniProgramTransportFailureKind ClassifyApiError(int errCode) =>
        errCode switch
        {
            45009 or 45011 or 45047 => WeChatMiniProgramTransportFailureKind.RateLimited,
            40001 or 40003 or 40013 or 40029 or 40163 or 43101 or 47003 => WeChatMiniProgramTransportFailureKind.Permanent,
            _ => WeChatMiniProgramTransportFailureKind.Transient,
        };
}
