using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native 原生产物上的最小关键 HTTP 链路断言。
/// </summary>
internal static class NativeApiE2EAssertions
{
    public const string AdminPassword = "FullNet!2026Integration";

    public static async Task VerifyCriticalHttpFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?>? settingsOverrides = null,
        CancellationToken cancellationToken = default)
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            NativeApiArtifactLocator.RequireArtifact(),
            provider,
            connectionString,
            settingsOverrides ?? new Dictionary<string, string?>(),
            TimeSpan.FromMinutes(2),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await LoginAsync(client, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);
        await VerifyAuthenticatedMeAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyTenancyReadAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyCodeGenerationCatalogReadAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifySerialNumbersFlowAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyDocumentFlowAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        var tenantToken = await EnterDevelopmentTenantAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyOrganizationReadAsync(client, tenantToken, cancellationToken)
            .ConfigureAwait(false);
        await VerifyReadinessAsync(client, cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task<string> LoginAsync(
        HttpClient client,
        string? nativeLogFilePath = null,
        CancellationToken cancellationToken = default)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", AdminPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken)
            .ConfigureAwait(false);
        if (loginResponse.StatusCode != HttpStatusCode.OK)
        {
            var errorBody = await loginResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var logTail = ReadNativeLogTail(nativeLogFilePath);
            Assert.Fail(
                $"Login failed ({loginResponse.StatusCode}): {errorBody}"
                + (string.IsNullOrEmpty(logTail)
                    ? string.Empty
                    : $"\nNative log tail:\n{logTail}"));
        }
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(token);
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.AccessToken));
        return token.AccessToken;
    }

    private static async Task VerifyAuthenticatedMeAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.AreEqual("admin", payload.RootElement.GetProperty("username").GetString());
    }

    private static async Task VerifyTenancyReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=1");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task VerifyOrganizationReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/units?page=1&pageSize=1");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    public static Task<string> EnterLocalTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken = default) =>
        EnterDevelopmentTenantAsync(client, hostAccessToken, cancellationToken);

    private static async Task<string> EnterDevelopmentTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                availableResponse,
                HttpStatusCode.OK,
                "List available tenants",
                cancellationToken)
            .ConfigureAwait(false);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(available);
        var developmentTenant = available.SingleOrDefault(tenant =>
            tenant.Identifier == "local");
        Assert.IsNotNull(
            developmentTenant,
            "Available tenants did not contain the Development seed 'local': "
                + string.Join(", ", available.Select(tenant =>
                    $"{tenant.Identifier} ({tenant.Id})")));

        using var enterRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(developmentTenant.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(entered);
        Assert.IsFalse(string.IsNullOrWhiteSpace(entered.AccessToken));
        return entered.AccessToken;
    }

    private static async Task VerifyCodeGenerationCatalogReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/code-generation/catalog/tables");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.IsTrue(payload.RootElement.ValueKind == JsonValueKind.Array);
    }

    private static async Task VerifySerialNumbersFlowAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        const string rulesPath = "/api/v1/serial-numbers/rules";
        var ruleKey = "native_aot_" + Guid.NewGuid().ToString("N");
        var create = new CreateSerialNumberRuleRequest(
            ruleKey,
            "Native AOT serial rule",
            null,
            SerialNumberRuleScope.Host,
            SerialNumberResetInterval.Never,
            "N-{sequence:4}",
            1,
            9999,
            1,
            true);

        using var createRequest = AuthorizedJson(
            HttpMethod.Post,
            rulesPath,
            accessToken,
            create);
        using var createResponse = await client.SendAsync(
                createRequest,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                createResponse,
                HttpStatusCode.Created,
                "Create Native AOT serial number rule",
                cancellationToken)
            .ConfigureAwait(false);
        var created = await createResponse.Content
            .ReadFromJsonAsync<SerialNumberRuleResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(created);
        Assert.AreEqual(ruleKey, created.RuleKey);

        using var getRequest = Authorized(
            HttpMethod.Get,
            $"{rulesPath}/{created.Id:D}",
            accessToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                getResponse,
                HttpStatusCode.OK,
                "Read Native AOT serial number rule",
                cancellationToken)
            .ConfigureAwait(false);
        var found = await getResponse.Content
            .ReadFromJsonAsync<SerialNumberRuleResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(created.Id, found?.Id);

        using var listRequest = Authorized(
            HttpMethod.Get,
            rulesPath + "?page=1&pageSize=20&key=" + Uri.EscapeDataString(ruleKey),
            accessToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                listResponse,
                HttpStatusCode.OK,
                "List Native AOT serial number rules",
                cancellationToken)
            .ConfigureAwait(false);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<SerialNumberRuleResponse>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));

        using var previewRequest = AuthorizedJson(
            HttpMethod.Post,
            rulesPath + "/preview",
            accessToken,
            new PreviewSerialNumberRequest(
                SerialNumberRuleScope.Host,
                "N-{sequence:4}",
                null,
                7,
                new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero)));
        using var previewResponse = await client.SendAsync(
                previewRequest,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                previewResponse,
                HttpStatusCode.OK,
                "Preview Native AOT serial number",
                cancellationToken)
            .ConfigureAwait(false);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<SerialNumberPreviewResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual("N-0007", preview?.Value);
    }

    private static async Task VerifyDocumentFlowAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var categoryRequest = new CreateHostDocumentCategoryRequest(
            "Native AOT category " + suffix,
            null,
            1);
        var category = await PostAndReadAsync<
                CreateHostDocumentCategoryRequest,
                HostDocumentCategoryResponse>(
                client,
                "/api/v1/document/host/categories/",
                accessToken,
                categoryRequest,
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        using (var duplicateCategoryRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/document/host/categories/",
                   accessToken,
                   categoryRequest))
        using (var duplicateCategoryResponse = await client
                   .SendAsync(duplicateCategoryRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    duplicateCategoryResponse,
                    HttpStatusCode.Conflict,
                    "Reject duplicate Native AOT document category",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        var tag = await PostAndReadAsync<
                CreateHostDocumentTagRequest,
                HostDocumentTagResponse>(
                client,
                "/api/v1/document/host/tags/",
                accessToken,
                new CreateHostDocumentTagRequest("Native AOT tag " + suffix),
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        var item = await PostAndReadAsync<
                CreateHostDocumentItemRequest,
                HostDocumentItemResponse>(
                client,
                "/api/v1/document/host/items/",
                accessToken,
                new CreateHostDocumentItemRequest(
                    "Native AOT document " + suffix,
                    "Native process closure",
                    HostDocumentType.Document,
                    HostDocumentStatus.Draft,
                    1,
                    null,
                    category.Id,
                    [tag.Id]),
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);

        using (var getRequest = Authorized(
                   HttpMethod.Get,
                   $"/api/v1/document/host/items/{item.Id:D}",
                   accessToken))
        using (var getResponse = await client.SendAsync(getRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    getResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT document item",
                    cancellationToken)
                .ConfigureAwait(false);
            var found = await getResponse.Content
                .ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(item.Id, found?.Id);
        }

        using (var listRequest = Authorized(
                   HttpMethod.Get,
                   "/api/v1/document/host/items/?page=1&pageSize=20",
                   accessToken))
        using (var listResponse = await client.SendAsync(listRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    listResponse,
                    HttpStatusCode.OK,
                    "List Native AOT document items",
                    cancellationToken)
                .ConfigureAwait(false);
            var page = await listResponse.Content
                .ReadFromJsonAsync<PagedResult<HostDocumentItemResponse>>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Items.Any(candidate => candidate.Id == item.Id));
        }

        var share = await PostAndReadAsync<
                CreateHostDocumentShareRequest,
                HostDocumentShareResponse>(
                client,
                "/api/v1/document/host/shares/",
                accessToken,
                new CreateHostDocumentShareRequest(item.Id, 1),
                HttpStatusCode.Created,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(item.Id, share.DocumentId);

        var permissionUserId = Guid.NewGuid();
        var permissions = await PostAndReadAsync<
                SetHostDocumentPermissionsRequest,
                IReadOnlyList<HostDocumentPermissionResponse>>(
                client,
                "/api/v1/document/host/permissions/",
                accessToken,
                new SetHostDocumentPermissionsRequest(
                    item.Id,
                    [new HostDocumentPermissionEntry(permissionUserId, "read")]),
                HttpStatusCode.OK,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.HasCount(1, permissions);
        Assert.AreEqual(item.Id, permissions[0].DocumentId);
        Assert.AreEqual(permissionUserId, permissions[0].UserId);
        Assert.AreEqual("read", permissions[0].PermissionLevel);

        using (var statisticsRequest = Authorized(
                   HttpMethod.Get,
                   "/api/v1/document/host/statistics/",
                   accessToken))
        using (var statisticsResponse = await client
                   .SendAsync(statisticsRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    statisticsResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT document statistics",
                    cancellationToken)
                .ConfigureAwait(false);
            var statistics = await statisticsResponse.Content
                .ReadFromJsonAsync<HostDocumentStatisticsResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(statistics);
            Assert.IsTrue(statistics.Summary.TotalItems >= 1);
            Assert.IsTrue(statistics.ByCategory.Any(entry => entry.Count >= 1));
            Assert.IsTrue(statistics.ShareCount >= 1);
        }

        foreach (var path in new[]
                 {
                     "/api/v1/document/host/categories/",
                     "/api/v1/document/host/tags/",
                     "/api/v1/document/host/shares/?page=1&pageSize=20",
                     "/api/v1/document/host/recycle-bin/?page=1&pageSize=20",
                 })
        {
            using var request = Authorized(HttpMethod.Get, path, accessToken);
            using var response = await client.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await AssertStatusAsync(
                    response,
                    HttpStatusCode.OK,
                    "Read Native AOT Document endpoint " + path,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<TResponse> PostAndReadAsync<TRequest, TResponse>(
        HttpClient client,
        string path,
        string accessToken,
        TRequest body,
        HttpStatusCode expectedStatusCode,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedJson(HttpMethod.Post, path, accessToken, body);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                response,
                expectedStatusCode,
                "POST " + path,
                cancellationToken)
            .ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(value);
        return value;
    }

    private static async Task VerifyReadinessAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/health/ready", cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static string ReadNativeLogTail(string? logFilePath, int maxChars = 4_000)
    {
        if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
        {
            return string.Empty;
        }

        var content = File.ReadAllText(logFilePath);
        if (content.Length <= maxChars)
        {
            return content;
        }

        return content[^maxChars..];
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(
        HttpMethod method,
        string path,
        string accessToken,
        T body)
    {
        var request = Authorized(method, path, accessToken);
        request.Content = JsonContent.Create(body);
        return request;
    }

    internal static async Task AssertStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (response.StatusCode == expectedStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        Assert.Fail(
            $"{operation} failed. Expected {expectedStatusCode}, actual {response.StatusCode}. "
            + $"Response body: {body}");
    }
}
