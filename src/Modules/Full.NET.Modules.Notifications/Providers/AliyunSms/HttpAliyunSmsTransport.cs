using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Notifications.Providers.AliyunSms;

/// <summary>通过 dysmsapi 经典 RPC 签名调用 SendSms。</summary>
internal sealed class HttpAliyunSmsTransport(IHttpClientFactory httpClientFactory) : IAliyunSmsTransport
{
    public const string HttpClientName = "Notifications.AliyunSms";

    private const string EndpointHost = "dysmsapi.aliyuncs.com";
    private const string ApiVersion = "2017-05-25";
    private const string Action = "SendSms";

    public async ValueTask<string> SendAsync(
        AliyunSmsSendCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = BuildParameters(command);
        var signature = Sign(command.AccessKeySecret, parameters);
        parameters["Signature"] = signature;

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://{EndpointHost}/")
        {
            Content = new FormUrlEncodedContent(parameters),
        };
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
            throw new AliyunSmsTransportException(
                AliyunSmsTransportFailureKind.Transient,
                "The SMS gateway could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AliyunSmsTransportException(
                    ClassifyHttpStatus(response.StatusCode),
                    "The SMS gateway returned an error response.");
            }

            return ParseBizId(body);
        }
    }

    private static Dictionary<string, string> BuildParameters(AliyunSmsSendCommand command)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccessKeyId"] = command.AccessKeyId,
            ["Action"] = Action,
            ["Format"] = "JSON",
            ["PhoneNumbers"] = command.PhoneNumber,
            ["RegionId"] = command.RegionId,
            ["SignName"] = command.SignName,
            ["SignatureMethod"] = "HMAC-SHA1",
            ["SignatureNonce"] = nonce,
            ["SignatureVersion"] = "1.0",
            ["TemplateCode"] = command.TemplateCode,
            ["TemplateParam"] = command.TemplateParamJson,
            ["Timestamp"] = timestamp,
            ["Version"] = ApiVersion,
        };
    }

    private static string Sign(string accessKeySecret, IReadOnlyDictionary<string, string> parameters)
    {
        var sorted = parameters
            .Where(pair => !string.Equals(pair.Key, "Signature", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{PercentEncode(pair.Key)}={PercentEncode(pair.Value)}");
        var canonicalized = string.Join("&", sorted);
        var stringToSign = $"POST&{PercentEncode("/")}&{PercentEncode(canonicalized)}";
        var key = Encoding.UTF8.GetBytes($"{accessKeySecret}&");
        var hash = HMACSHA1.HashData(key, Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hash);
    }

    private static string PercentEncode(string value)
    {
        var encoded = Uri.EscapeDataString(value);
        return encoded
            .Replace("+", "%20", StringComparison.Ordinal)
            .Replace("*", "%2A", StringComparison.Ordinal)
            .Replace("%7E", "~", StringComparison.Ordinal);
    }

    private static string ParseBizId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = root.TryGetProperty("Code", out var codeElement)
                ? codeElement.GetString()
                : null;
            if (!string.Equals(code, "OK", StringComparison.Ordinal))
            {
                throw new AliyunSmsTransportException(
                    ClassifyApiCode(code),
                    "The SMS gateway rejected the request.");
            }

            if (!root.TryGetProperty("BizId", out var bizIdElement)
                || bizIdElement.GetString() is not { Length: > 0 } bizId)
            {
                throw new AliyunSmsTransportException(
                    AliyunSmsTransportFailureKind.Transient,
                    "The SMS gateway response did not include a message identifier.");
            }

            return bizId;
        }
        catch (JsonException)
        {
            throw new AliyunSmsTransportException(
                AliyunSmsTransportFailureKind.Transient,
                "The SMS gateway response was invalid.");
        }
    }

    private static AliyunSmsTransportFailureKind ClassifyHttpStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests => AliyunSmsTransportFailureKind.RateLimited,
            >= System.Net.HttpStatusCode.InternalServerError => AliyunSmsTransportFailureKind.Transient,
            _ => AliyunSmsTransportFailureKind.Permanent,
        };

    private static AliyunSmsTransportFailureKind ClassifyApiCode(string? code) =>
        code switch
        {
            null => AliyunSmsTransportFailureKind.Transient,
            "isv.BUSINESS_LIMIT_CONTROL" or "isv.DAY_LIMIT_CONTROL" or "isv.MONTH_LIMIT_CONTROL"
                => AliyunSmsTransportFailureKind.RateLimited,
            "isp.RAM_PERMISSION_DENY" or "isv.INVALID_PARAMETERS" or "isv.SMS_SIGNATURE_ILLEGAL"
                or "isv.SMS_TEMPLATE_ILLEGAL" or "isv.MOBILE_NUMBER_ILLEGAL"
                => AliyunSmsTransportFailureKind.Permanent,
            _ => AliyunSmsTransportFailureKind.Transient,
        };
}
