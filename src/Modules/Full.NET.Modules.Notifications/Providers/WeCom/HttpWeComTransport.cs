using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Full.NET.Modules.Notifications.Providers.WeCom;

/// <summary>通过企业微信开放平台调用 gettoken 与 message/send 文本消息。</summary>
internal sealed class HttpWeComTransport(IHttpClientFactory httpClientFactory) : IWeComTransport
{
    public const string HttpClientName = "Notifications.WeCom";

    private const string TokenEndpoint = "https://qyapi.weixin.qq.com/cgi-bin/gettoken";
    private const string SendEndpoint = "https://qyapi.weixin.qq.com/cgi-bin/message/send";

    public async ValueTask<WeComAccessToken> GetAccessTokenAsync(
        string corpId,
        string corpSecret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url =
            $"{TokenEndpoint}?corpid={Uri.EscapeDataString(corpId)}&corpsecret={Uri.EscapeDataString(corpSecret)}";
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
            throw new WeComTransportException(
                WeComTransportFailureKind.Transient,
                "The WeCom token endpoint could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeComTransportException(
                    ClassifyHttpStatus(response.StatusCode),
                    "The WeCom token endpoint returned an error response.");
            }

            return ParseAccessToken(body);
        }
    }

    /// <summary>发送企业微信文本消息并保留收件目标与安全标志。</summary>
    /// <param name="accessToken">提供程序授权令牌。</param>
    /// <param name="command">已经编译和校验的外部调用参数。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public async ValueTask<string> SendTextAsync(
        string accessToken,
        WeComSendTextCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = new JsonObject
        {
            ["touser"] = command.ToUserId,
            ["msgtype"] = "text",
            ["agentid"] = command.AgentId,
            ["text"] = new JsonObject
            {
                ["content"] = command.Content,
            },
            ["safe"] = 0,
        }.ToJsonString();
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SendEndpoint}?access_token={Uri.EscapeDataString(accessToken)}")
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
            throw new WeComTransportException(
                WeComTransportFailureKind.Transient,
                "The WeCom message endpoint could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeComTransportException(
                    ClassifyHttpStatus(response.StatusCode),
                    "The WeCom message endpoint returned an error response.");
            }

            return ParseMsgId(body);
        }
    }

    private static WeComAccessToken ParseAccessToken(string body)
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
                throw new WeComTransportException(
                    ClassifyApiError(errCode),
                    "The WeCom token endpoint rejected the request.");
            }

            if (!root.TryGetProperty("access_token", out var tokenElement)
                || tokenElement.GetString() is not { Length: > 0 } token)
            {
                throw new WeComTransportException(
                    WeComTransportFailureKind.Transient,
                    "The WeCom token response did not include an access token.");
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt32()
                : 7200;
            if (expiresIn < 60)
            {
                expiresIn = 60;
            }

            return new WeComAccessToken(token, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }
        catch (JsonException)
        {
            throw new WeComTransportException(
                WeComTransportFailureKind.Transient,
                "The WeCom token response was invalid.");
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
                throw new WeComTransportException(
                    ClassifyApiError(errCode),
                    "The WeCom message endpoint rejected the request.");
            }

            if (root.TryGetProperty("msgid", out var msgIdElement)
                && msgIdElement.GetString() is { Length: > 0 } msgId)
            {
                return msgId;
            }

            return Guid.NewGuid().ToString("N");
        }
        catch (JsonException)
        {
            throw new WeComTransportException(
                WeComTransportFailureKind.Transient,
                "The WeCom message response was invalid.");
        }
    }

    private static WeComTransportFailureKind ClassifyHttpStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests => WeComTransportFailureKind.RateLimited,
            >= System.Net.HttpStatusCode.InternalServerError => WeComTransportFailureKind.Transient,
            _ => WeComTransportFailureKind.Permanent,
        };

    private static WeComTransportFailureKind ClassifyApiError(int errCode) =>
        errCode switch
        {
            45009 or 45011 => WeComTransportFailureKind.RateLimited,
            40014 or 40084 or 42001 or 60020 => WeComTransportFailureKind.Permanent,
            _ => WeComTransportFailureKind.Transient,
        };
}
