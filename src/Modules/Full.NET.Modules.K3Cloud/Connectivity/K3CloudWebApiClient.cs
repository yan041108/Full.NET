using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.K3Cloud.Persistence;

namespace Full.NET.Modules.K3Cloud.Connectivity;

/// <summary>金蝶 K3Cloud WebAPI 客户端；会话基于 Cookie，禁止通用远程方法反射调用。</summary>
internal sealed class K3CloudWebApiClient
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

    /// <summary>登录后执行 Save 与 Submit 链式调用。</summary>
    public async Task<(bool Succeeded, string? BillId, string? BillNo, string Message)> SaveAndSubmitAsync(
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
            return (false, null, null, login.Message);
        }

        var saveBody = BuildRequestBody([formId, payloadJson]);
        var saveResponse = await PostAsync(client, config.BaseUrl, SavePath, saveBody, cancellationToken)
            .ConfigureAwait(false);
        if (!Domain.K3CloudResponseParser.TryParseSaveResult(saveResponse, out var billId, out var billNo, out var saveMessage))
        {
            return (false, null, null, saveMessage);
        }

        var submitPayload = Domain.K3CloudResponseParser.BuildSubmitPayload(billId, billNo);
        var submitBody = BuildRequestBody([formId, submitPayload]);
        var submitResponse = await PostAsync(client, config.BaseUrl, SubmitPath, submitBody, cancellationToken)
            .ConfigureAwait(false);
        if (!Domain.K3CloudResponseParser.TryParseSubmitSuccess(submitResponse, out var submitMessage))
        {
            return (false, billId, billNo, submitMessage);
        }

        return (true, billId, billNo, submitMessage);
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
