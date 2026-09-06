using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Notifications.Providers.DingTalk;

/// <summary>通过钉钉开放平台调用 gettoken 与 createAndDeliver。</summary>
internal sealed class HttpDingTalkTransport(IHttpClientFactory httpClientFactory) : IDingTalkTransport
{
    public const string HttpClientName = "Notifications.DingTalk";

    private const string TokenEndpoint = "https://oapi.dingtalk.com/gettoken";
    private const string CreateAndDeliverEndpoint =
        "https://api.dingtalk.com/v1.0/card/instances/createAndDeliver";

    public async ValueTask<DingTalkAccessToken> GetAccessTokenAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url =
            $"{TokenEndpoint}?appkey={Uri.EscapeDataString(appKey)}&appsecret={Uri.EscapeDataString(appSecret)}";
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
            throw new DingTalkTransportException(
                DingTalkTransportFailureKind.Transient,
                "The DingTalk token endpoint could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DingTalkTransportException(
                    ClassifyHttpStatus(response.StatusCode),
                    "The DingTalk token endpoint returned an error response.");
            }

            return ParseAccessToken(body);
        }
    }

    public async ValueTask<string> CreateAndDeliverAsync(
        string accessToken,
        DingTalkCreateAndDeliverCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = BuildPayload(command);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, CreateAndDeliverEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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
            throw new DingTalkTransportException(
                DingTalkTransportFailureKind.Transient,
                "The DingTalk card endpoint could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DingTalkTransportException(
                    ClassifyHttpStatus(response.StatusCode),
                    "The DingTalk card endpoint returned an error response.");
            }

            return ParseCreateAndDeliverResponse(body, command.OutTrackId);
        }
    }

    private static string BuildPayload(DingTalkCreateAndDeliverCommand command)
    {
        var cardParamMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in command.CardParamMap)
        {
            cardParamMap[pair.Key] = pair.Value;
        }

        var root = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cardTemplateId"] = command.CardTemplateId,
            ["outTrackId"] = command.OutTrackId,
            ["openSpaceId"] = command.OpenSpaceId,
            ["cardData"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["cardParamMap"] = cardParamMap,
            },
            ["imRobotOpenSpaceModel"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["supportForward"] = false,
            },
            ["imRobotOpenDeliverModel"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["spaceType"] = "IM_ROBOT",
                ["robotCode"] = command.RobotCode,
            },
        };
        if (!string.IsNullOrWhiteSpace(command.CallbackRouteKey))
        {
            root["callbackRouteKey"] = command.CallbackRouteKey;
        }

        return JsonSerializer.Serialize(root);
    }

    private static DingTalkAccessToken ParseAccessToken(string body)
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
                throw new DingTalkTransportException(
                    ClassifyTokenError(errCode),
                    "The DingTalk token endpoint rejected the request.");
            }

            if (!root.TryGetProperty("access_token", out var tokenElement)
                || tokenElement.GetString() is not { Length: > 0 } token)
            {
                throw new DingTalkTransportException(
                    DingTalkTransportFailureKind.Transient,
                    "The DingTalk token response did not include an access token.");
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt32()
                : 7200;
            if (expiresIn < 60)
            {
                expiresIn = 60;
            }

            return new DingTalkAccessToken(
                token,
                DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }
        catch (JsonException)
        {
            throw new DingTalkTransportException(
                DingTalkTransportFailureKind.Transient,
                "The DingTalk token response was invalid.");
        }
    }

    private static string ParseCreateAndDeliverResponse(string body, string fallbackOutTrackId)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("success", out var successElement)
                && successElement.ValueKind == JsonValueKind.False)
            {
                throw new DingTalkTransportException(
                    DingTalkTransportFailureKind.Permanent,
                    "The DingTalk card endpoint rejected the request.");
            }

            if (root.TryGetProperty("result", out var resultElement)
                && resultElement.TryGetProperty("outTrackId", out var outTrackIdElement)
                && outTrackIdElement.GetString() is { Length: > 0 } outTrackId)
            {
                return outTrackId;
            }

            return fallbackOutTrackId;
        }
        catch (JsonException)
        {
            throw new DingTalkTransportException(
                DingTalkTransportFailureKind.Transient,
                "The DingTalk card response was invalid.");
        }
    }

    private static DingTalkTransportFailureKind ClassifyHttpStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests => DingTalkTransportFailureKind.RateLimited,
            >= System.Net.HttpStatusCode.InternalServerError => DingTalkTransportFailureKind.Transient,
            _ => DingTalkTransportFailureKind.Permanent,
        };

    private static DingTalkTransportFailureKind ClassifyTokenError(int errCode) =>
        errCode switch
        {
            88 or 90018 => DingTalkTransportFailureKind.RateLimited,
            40014 or 40089 or 40091 => DingTalkTransportFailureKind.Permanent,
            _ => DingTalkTransportFailureKind.Transient,
        };
}
