using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.K3Cloud.Persistence;

namespace Full.NET.Modules.K3Cloud.Connectivity;

/// <summary>金蝶 K3Cloud WebAPI 客户端；会话基于 Cookie，禁止通用远程方法反射调用。</summary>
internal sealed class K3CloudWebApiClient : IK3CloudWebApiClient
{
    public const string HttpClientName = "Full.NET.K3Cloud.WebApi";

    private const string ValidateUserPath =
        "/K3Cloud/Kingdee.BOS.WebApi.ServicesStub.AuthService.ValidateUser.common.kdsvc";

    private const string SavePath =
        "/K3Cloud/Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.Save.common.kdsvc";

    private const string SubmitPath =
        "/K3Cloud/Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.Submit.common.kdsvc";

    /// <summary>执行 ValidateUser 并返回响应正文。</summary>
    public async Task<(bool Succeeded, string ResponseBody, string Message)> ValidateUserAsync(
        K3CloudConnectionConfigRecord config,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateSessionClient();
        var body = BuildRequestBody([
            config.AcctId,
            config.Username,
            password,
            config.Lcid.ToString()]);
        var responseBody = await PostAsync(client, config.BaseUrl, ValidateUserPath, body, cancellationToken)
            .ConfigureAwait(false);
        var succeeded = Domain.K3CloudResponseParser.TryParseLoginSuccess(responseBody, out var message);
        return (succeeded, responseBody, message);
    }

    /// <summary>登录后执行 Save。</summary>
    public async Task<(bool Succeeded, string ResponseBody, string Message)> SaveAsync(
        K3CloudConnectionConfigRecord config,
        string password,
        string formId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateSessionClient();
        var login = await LoginAsync(client, config, password, cancellationToken).ConfigureAwait(false);
        if (!login.Succeeded)
        {
            return (false, string.Empty, login.Message);
        }

        var body = BuildRequestBody([formId, payloadJson]);
        var responseBody = await PostAsync(client, config.BaseUrl, SavePath, body, cancellationToken)
            .ConfigureAwait(false);
        var succeeded = Domain.K3CloudResponseParser.TryParseSaveResult(
            responseBody,
            out _,
            out _,
            out var message);
        return (succeeded, responseBody, message);
    }

    /// <inheritdoc />
    public async Task<(bool Succeeded, string? BillId, string? BillNo, string Message)> SaveDocumentAsync(
        K3CloudConnectionConfigRecord config, string password, string formId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateSessionClient();
        var login = await LoginAsync(client, config, password, cancellationToken).ConfigureAwait(false);
        if (!login.Succeeded) return (false, null, null, login.Message);
        var response = await PostAsync(client, config.BaseUrl, SavePath,
            BuildRequestBody([formId, payloadJson]), cancellationToken).ConfigureAwait(false);
        var success = ReadExplicitSuccess(response);
        var parsed = Domain.K3CloudResponseParser.TryParseSaveResult(response, out var id, out var number, out var message);
        if (success && !parsed) throw new JsonException("Save succeeded without a document identity.");
        return (parsed, id, number, message);
    }

    /// <inheritdoc />
    public async Task<(bool Succeeded, string Message)> SubmitDocumentAsync(
        K3CloudConnectionConfigRecord config, string password, string formId, string? billId, string? billNo,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateSessionClient();
        var login = await LoginAsync(client, config, password, cancellationToken).ConfigureAwait(false);
        if (!login.Succeeded) return (false, login.Message);
        var payload = Domain.K3CloudResponseParser.BuildSubmitPayload(billId ?? "", billNo ?? "");
        var response = await PostAsync(client, config.BaseUrl, SubmitPath,
            BuildRequestBody([formId, payload]), cancellationToken).ConfigureAwait(false);
        ReadExplicitSuccess(response);
        var success = Domain.K3CloudResponseParser.TryParseSubmitSuccess(response, out var message);
        return (success, message);
    }

    /// <summary>缺失或畸形响应属于结果未知，禁止伪装成确定失败后重发 Save。</summary>
    internal static bool ReadExplicitSuccess(string response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("Result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("ResponseStatus", out var status) || status.ValueKind != JsonValueKind.Object
            || !status.TryGetProperty("IsSuccess", out var success)
            || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException("K3Cloud response does not provide an explicit operation result.");
        }
        return success.GetBoolean();
    }

    private static async Task<(bool Succeeded, string Message)> LoginAsync(
        HttpClient client,
        K3CloudConnectionConfigRecord config,
        string password,
        CancellationToken cancellationToken)
    {
        var body = BuildRequestBody([
            config.AcctId,
            config.Username,
            password,
            config.Lcid.ToString()]);
        var responseBody = await PostAsync(client, config.BaseUrl, ValidateUserPath, body, cancellationToken)
            .ConfigureAwait(false);
        var succeeded = Domain.K3CloudResponseParser.TryParseLoginSuccess(responseBody, out var message);
        return (succeeded, message);
    }

    private static HttpClient CreateSessionClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        return client;
    }

    private static async Task<string> PostAsync(
        HttpClient client,
        string baseUrl,
        string path,
        string body,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(new Uri(NormalizeBaseUrl(baseUrl)), path);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按金蝶 WebAPI 协议构造请求，保持参数次序与 JSON 类型。</summary>
    /// <param name="parameters">按协议传递的参数集合。</param>
    private static string BuildRequestBody(string[] parameters)
    {
        var payload = new JsonObject
        {
            ["format"] = 1,
            ["useragent"] = "Full.NET",
            ["rid"] = Guid.NewGuid().ToString("N"),
            ["parameters"] = new JsonArray(parameters.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["v"] = "1.0",
        };
        return payload.ToJsonString();
    }

    private static string NormalizeBaseUrl(string baseUrl) =>
        baseUrl.TrimEnd('/') + "/";
}
