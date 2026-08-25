using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native 原生产物上的 Settings HTTP/JSON 链路断言。
/// </summary>
internal static class NativeApiSettingsE2EAssertions
{
    public static async Task VerifySettingsFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            new Dictionary<string, string?>(),
            TimeSpan.FromMinutes(3),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(
                client,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        await VerifyHostDictionaryLifecycleAsync(client, token, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);
        await VerifyConfigAndDiagnosticAsync(client, token, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);
        await VerifyGridPreferenceAsync(client, token, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);

        var tenantToken = await NativeApiE2EAssertions.EnterLocalTenantAsync(
                client,
                token,
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyTenantDictionaryListAsync(
                client,
                tenantToken,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    private static async Task VerifyHostDictionaryLifecycleAsync(
        HttpClient client,
        string token,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        var code = $"aotdt-{Guid.NewGuid():N}"[..12];
        using var createRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/settings/dict-types",
            token,
            new CreateDictTypeRequest(code, "Native AOT 字典", "native-aot", 10));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                createResponse,
                HttpStatusCode.Created,
                "Create host dict type",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var created = await createResponse.Content
            .ReadFromJsonAsync<DictTypeResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(created);
        Assert.AreEqual(code, created.Code);

        using var listRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/settings/dict-types?page=1&pageSize=20",
            token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                listResponse,
                HttpStatusCode.OK,
                "List host dict types",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<DictTypeResponse>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));

        using var itemRequest = AuthorizedJson(
            HttpMethod.Post,
            $"/api/v1/settings/dict-types/{created.Id:D}/items",
            token,
            new CreateDictItemRequest("AOT 项", "aot_item", null, 10));
        using var itemResponse = await client.SendAsync(itemRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                itemResponse,
                HttpStatusCode.Created,
                "Create host dict item",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var item = await itemResponse.Content
            .ReadFromJsonAsync<DictItemResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(item);
        Assert.AreEqual("aot_item", item.Value);

        using var itemsRequest = Authorized(
            HttpMethod.Get,
            $"/api/v1/settings/dict-types/{created.Id:D}/items?page=1&pageSize=20",
            token);
        using var itemsResponse = await client.SendAsync(itemsRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                itemsResponse,
                HttpStatusCode.OK,
                "List host dict items",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyConfigAndDiagnosticAsync(
        HttpClient client,
        string token,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var configRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/settings/config-entries?page=1&pageSize=20",
            token);
        using var configResponse = await client.SendAsync(configRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                configResponse,
                HttpStatusCode.OK,
                "List host config entries",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var configs = await configResponse.Content
            .ReadFromJsonAsync<PagedResult<ConfigEntryResponse>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(configs);

        using var enumRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs",
            token);
        using var enumResponse = await client.SendAsync(enumRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                enumResponse,
                HttpStatusCode.OK,
                "List enum catalogs",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        using var policyRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/settings/diagnostic-policy",
            token);
        using var policyResponse = await client.SendAsync(policyRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                policyResponse,
                HttpStatusCode.OK,
                "Get diagnostic policy",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var policy = await policyResponse.Content
            .ReadFromJsonAsync<DiagnosticPolicyResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(policy);
    }

    private static async Task VerifyGridPreferenceAsync(
        HttpClient client,
        string token,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var getRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/me/grid-preferences/identity.users",
            token);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                getResponse,
                HttpStatusCode.OK,
                "Get grid preference",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var current = await getResponse.Content
            .ReadFromJsonAsync<GridPreferenceResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(current);

        using var putRequest = AuthorizedJson(
            HttpMethod.Put,
            "/api/v1/me/grid-preferences/identity.users",
            token,
            new UpdateGridPreferenceRequest(
                current.SchemaVersion,
                [new GridColumnPreference("displayName", 0, 160, true, null)],
                current.Version));
        using var putResponse = await client.SendAsync(putRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                putResponse,
                HttpStatusCode.OK,
                "Put grid preference",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyTenantDictionaryListAsync(
        HttpClient client,
        string tenantToken,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(
            HttpMethod.Get,
            "/api/v1/settings/tenant-dict-types?page=1&pageSize=20",
            tenantToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                response,
                HttpStatusCode.OK,
                "List tenant dict types",
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<DictTypeResponse>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(
        HttpMethod method,
        string url,
        string token,
        T body)
    {
        var request = Authorized(method, url, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task AssertOkAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string operation,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        var logTail = File.Exists(logFilePath)
            ? File.ReadAllText(logFilePath)
            : string.Empty;
        if (logTail.Length > 4000)
        {
            logTail = logTail[^4000..];
        }

        Assert.Fail(
            $"{operation} failed. Expected {expected}, actual {response.StatusCode}. "
            + $"Response body: {body}"
            + (string.IsNullOrEmpty(logTail) ? string.Empty : $"\nNative log tail:\n{logTail}"));
    }
}
