using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

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
        await VerifyAuditingFlowAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyMessagingDeadLetterFlowAsync(
                provider,
                connectionString,
                client,
                token,
                cancellationToken)
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
        using (var request = Authorized(
                   HttpMethod.Get,
                   "/api/v1/code-generation/catalog/tables",
                   accessToken))
        using (var response = await client.SendAsync(request, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    response,
                    HttpStatusCode.OK,
                    "Read Native AOT code generation catalog tables",
                    cancellationToken)
                .ConfigureAwait(false);
            var tables = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<CodeGenerationCatalogTableResponse>>(
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(tables);
            Assert.IsNotEmpty(tables);

            using var columnsRequest = Authorized(
                HttpMethod.Get,
                "/api/v1/code-generation/catalog/tables/"
                    + Uri.EscapeDataString(tables[0].TableName)
                    + "/columns",
                accessToken);
            using var columnsResponse = await client
                .SendAsync(columnsRequest, cancellationToken)
                .ConfigureAwait(false);
            await AssertStatusAsync(
                    columnsResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT code generation catalog columns",
                    cancellationToken)
                .ConfigureAwait(false);
            var columns = await columnsResponse.Content
                .ReadFromJsonAsync<CodeGenerationCatalogColumnListResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(columns);
            Assert.IsNotEmpty(columns.Columns);
        }

        var suffix = Guid.NewGuid().ToString("N");
        var schema = new CodeGenerationPreviewRequest(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "TenantRequired",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
                new("TenantId", "TenantId", "tenantId", "Uuid", false, null, null, null),
                new("Name", "Name", "displayName", "String", false, 200, null, null),
                new("IsActive", "IsActive", "isActive", "Boolean", false, null, null, null),
                new("Version", "Version", "version", "Int64", false, null, null, null),
            ]);
        using var createTemplateRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/code-generation/templates",
            accessToken,
            new CreateCodeGenerationTemplateRequest(
                "Native AOT " + suffix,
                "Native AOT materializer verification",
                schema));
        using var createTemplateResponse = await client
            .SendAsync(createTemplateRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                createTemplateResponse,
                HttpStatusCode.Created,
                "Create Native AOT code generation template",
                cancellationToken)
            .ConfigureAwait(false);
        var template = await createTemplateResponse.Content
            .ReadFromJsonAsync<CodeGenerationTemplateResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(template);

        using var previewRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/code-generation/runs/preview",
            accessToken,
            new CodeGenerationRunPreviewRequest(template.Id, template.Version, null));
        using var previewResponse = await client.SendAsync(previewRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                previewResponse,
                HttpStatusCode.OK,
                "Create Native AOT code generation preview run",
                cancellationToken)
            .ConfigureAwait(false);
        var run = await previewResponse.Content
            .ReadFromJsonAsync<CodeGenerationRunPreviewResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(run);

        await AssertPageContainsAsync<CodeGenerationTemplateResponse>(
                client,
                "/api/v1/code-generation/templates?page=1&pageSize=20",
                accessToken,
                item => item.Id == template.Id,
                cancellationToken)
            .ConfigureAwait(false);
        await AssertPageContainsAsync<CodeGenerationRunResponse>(
                client,
                "/api/v1/code-generation/runs?page=1&pageSize=20",
                accessToken,
                item => item.Id == run.RunId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task AssertPageContainsAsync<T>(
        HttpClient client,
        string path,
        string accessToken,
        Func<T, bool> predicate,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, path, accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                response,
                HttpStatusCode.OK,
                "Read Native AOT page " + path,
                cancellationToken)
            .ConfigureAwait(false);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<T>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(predicate), "Expected Native AOT page row was not materialized.");
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

    private static async Task VerifyAuditingFlowAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var outboundProviderKey = "native-aot-" + Guid.NewGuid().ToString("N");
        using (var outboundProbeRequest = AuthorizedJson(
                   HttpMethod.Post,
                   "/api/v1/auditing/outbound-call-probes",
                   accessToken,
                   new OutboundCallAuditProbeRequest(
                       new OutboundCallAuditRequest(
                           outboundProviderKey,
                           "record",
                           "test",
                           204,
                           true,
                           1,
                           0))))
        using (var outboundProbeResponse = await client
                   .SendAsync(outboundProbeRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    outboundProbeResponse,
                    HttpStatusCode.NoContent,
                    "Write Native AOT outbound audit probe",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using (var exceptionProbeRequest = Authorized(
                   HttpMethod.Post,
                   "/api/v1/auditing/exception-probes",
                   accessToken))
        using (var exceptionProbeResponse = await client
                   .SendAsync(exceptionProbeRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    exceptionProbeResponse,
                    HttpStatusCode.InternalServerError,
                    "Write Native AOT exception audit probe",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using (var accessRequest = Authorized(
                   HttpMethod.Get,
                   "/api/v1/auditing/access-logs?page=1&pageSize=100",
                   accessToken))
        using (var accessResponse = await client.SendAsync(accessRequest, cancellationToken)
                   .ConfigureAwait(false))
        {
            await AssertStatusAsync(
                    accessResponse,
                    HttpStatusCode.OK,
                    "Read Native AOT access audit page",
                    cancellationToken)
                .ConfigureAwait(false);
            var page = await accessResponse.Content
                .ReadFromJsonAsync<PagedResult<AccessLogResponse>>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(page);
        }
        _ = await WaitForAuditPageItemAsync<OperationLogResponse>(
                client,
                "/api/v1/auditing/operation-logs?page=1&pageSize=100",
                accessToken,
                static item => item.RequestPath.Contains("/api/v1/", StringComparison.Ordinal),
                cancellationToken)
            .ConfigureAwait(false);
        _ = await WaitForAuditPageItemAsync<ExceptionLogResponse>(
                client,
                "/api/v1/auditing/exception-logs?page=1&pageSize=100",
                accessToken,
                static item => item.RequestPath == "/api/v1/auditing/exception-probes",
                cancellationToken)
            .ConfigureAwait(false);
        _ = await WaitForAuditPageItemAsync<OutboundCallLogResponse>(
                client,
                "/api/v1/auditing/outbound-call-logs?page=1&pageSize=100",
                accessToken,
                item => item.ProviderKey == outboundProviderKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<T> WaitForAuditPageItemAsync<T>(
        HttpClient client,
        string path,
        string accessToken,
        Func<T, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var request = Authorized(HttpMethod.Get, path, accessToken);
            using var response = await client.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await AssertStatusAsync(
                    response,
                    HttpStatusCode.OK,
                    "Read Native AOT audit page " + path,
                    cancellationToken)
                .ConfigureAwait(false);
            var page = await response.Content.ReadFromJsonAsync<PagedResult<T>>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(page);
            var match = page.Items.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                .ConfigureAwait(false);
        }

        Assert.Fail("Timed out waiting for Native AOT audit row from " + path);
        throw new InvalidOperationException("Unreachable after Assert.Fail.");
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

    private static async Task VerifyMessagingDeadLetterFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        const string consumerName = "fullnet.native_aot.unregistered.consumer";
        const string messageType = "fullnet.native_aot.messaging.materializer_probe";
        var messageId = Guid.CreateVersion7();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Probe = messageId });
        await SeedFailedDeadLetterAsync(
                provider,
                connectionString,
                consumerName,
                messageId,
                messageType,
                payload,
                cancellationToken)
            .ConfigureAwait(false);

        await AssertPageContainsAsync<DeadLetterResponse>(
                client,
                "/api/v1/messaging/dead-letters?page=1&pageSize=20&consumerName="
                    + Uri.EscapeDataString(consumerName),
                accessToken,
                item => item.ConsumerName == consumerName && item.MessageId == messageId,
                cancellationToken)
            .ConfigureAwait(false);

        using var replayRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/messaging/dead-letters/replay",
            accessToken,
            new ReplayDeadLetterRequest(consumerName, messageId));
        using var replayResponse = await client.SendAsync(replayRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertStatusAsync(
                replayResponse,
                HttpStatusCode.UnprocessableEntity,
                "Replay Native AOT dead letter through the Outbox envelope materializer",
                cancellationToken)
            .ConfigureAwait(false);
        using var problem = JsonDocument.Parse(
            await replayResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.AreEqual(
            MessagingErrorCodes.SubscriptionRouteNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task SeedFailedDeadLetterAsync(
        DatabaseProvider provider,
        string connectionString,
        string consumerName,
        Guid messageId,
        string messageType,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            ConsumerName = consumerName,
            MessageId = messageId,
            MessageType = messageType,
            SchemaVersion = 1,
            ContentType = "application/json",
            PartitionKey = $"native-aot-{messageId:N}",
            Producer = "fullnet.native_aot.tests",
            Payload = payload,
            PayloadHash = SHA256.HashData(payload),
            LastErrorCode = "messaging.native_aot.probe",
            LastError = "Injected dead letter for Native AOT materializer verification.",
        };
        var command = provider switch
        {
            DatabaseProvider.SqlServer => new CommandDefinition(
                """
                INSERT INTO dbo.fn_messaging_outbox_event
                    (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                     CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
                VALUES
                    (@MessageId, @MessageType, @SchemaVersion, @ContentType, NULL, @PartitionKey,
                     NULL, NULL, NULL, @Producer, @Payload, SYSDATETIMEOFFSET());

                INSERT INTO dbo.fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId, PayloadHash,
                     Status, Attempts, ReceivedAtUtc, ProcessedAtUtc, LastErrorCode, LastError)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL, @PayloadHash,
                     'failed', 1, SYSDATETIMEOFFSET(), NULL, @LastErrorCode, @LastError);
                """,
                parameters,
                cancellationToken: cancellationToken),
            DatabaseProvider.MySql => new CommandDefinition(
                """
                INSERT INTO fn_messaging_outbox_event
                    (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                     CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
                VALUES
                    (@MessageId, @MessageType, @SchemaVersion, @ContentType, NULL, @PartitionKey,
                     NULL, NULL, NULL, @Producer, @Payload, UTC_TIMESTAMP(6));

                INSERT INTO fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId, PayloadHash,
                     Status, Attempts, ReceivedAtUtc, ProcessedAtUtc, LastErrorCode, LastError)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL, @PayloadHash,
                     'failed', 1, UTC_TIMESTAMP(6), NULL, @LastErrorCode, @LastError);
                """,
                parameters,
                cancellationToken: cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{provider}'."),
        };

        if (provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(command).ConfigureAwait(false);
            return;
        }

        await using var mySqlConnection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await mySqlConnection.ExecuteAsync(command).ConfigureAwait(false);
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
