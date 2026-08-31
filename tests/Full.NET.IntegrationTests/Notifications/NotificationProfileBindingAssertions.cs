using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// Profile/Binding 控制面：空目录未知类型失败、密钥不回显、Host 默认不共享、
/// Binding 发布固定版本，以及 Intent 固定 BindingVersion 并写入 accepted Delivery（Attempt=0，不调用 Adapter）。
/// </summary>
internal static class NotificationProfileBindingAssertions
{
    private const string SecretReference = "vault://test/notifications-api-token";

    public static void ConfigureTestServices(IServiceCollection services)
    {
        services.TryAddSingleton<TestNotificationProviderHarness>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INotificationProviderAdapter, TestNotificationProvider>());
        services.TryAddSingleton<TestEndpointNotificationProviderHarness>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INotificationProviderAdapter, TestEndpointNotificationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INotificationReceiptVerifier, TestNotificationReceiptVerifier>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INotificationReceiptVerifier, AlternateTestNotificationReceiptVerifier>());
    }

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var hostClient = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("localhost");
        var hostAdminToken = await LoginAsHostAdminAsync(hostClient, cancellationToken);
        var tenantSwitchToken = await LoginAsHostAdminAsync(tenantClient, cancellationToken);
        var hostUser = await GetCurrentUserAsync(hostClient, hostAdminToken, cancellationToken);

        using var forbidden = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/provider-profiles",
            await factory.CreateHostAccessTokenAsync(
                [NotificationPlatformPermissions.ProviderProfilesRead],
                cancellationToken),
            CreateProfileBody($"deny-{Guid.NewGuid():N}"[..20]));
        using var forbiddenResponse = await hostClient.SendAsync(forbidden, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var typesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/notifications/provider-types");
        typesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var typesResponse = await hostClient.SendAsync(typesRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, typesResponse.StatusCode);
        var types = await typesResponse.Content
            .ReadFromJsonAsync<NotificationProviderTypeDescriptor[]>(cancellationToken);
        Assert.IsNotNull(types);
        var testType = types.Single(item => item.ProviderTypeKey == TestNotificationProvider.ProviderTypeKeyValue);
        Assert.AreEqual(TestNotificationProvider.AdapterVersionValue, testType.AdapterVersion);
        CollectionAssert.Contains(testType.SupportedChannelKeys.ToArray(), TestNotificationProvider.ChannelKey);
        CollectionAssert.Contains(testType.SecretFieldKeys.ToArray(), TestNotificationProvider.SecretFieldKey);

        using var unknown = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/provider-profiles",
            hostAdminToken,
            CreateProfileBody($"unk-{Guid.NewGuid():N}"[..16], providerTypeKey: "smtp.mailgun"));
        using var unknownResponse = await hostClient.SendAsync(unknown, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        var unknownBody = await unknownResponse.Content.ReadAsStringAsync(cancellationToken);
        AssertProblem(unknownBody, NotificationsErrorCodes.ProviderTypeUnknown);

        using var leakedSecret = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/provider-profiles",
            hostAdminToken,
            new
            {
                profileKey = $"sec-{Guid.NewGuid():N}"[..16],
                providerTypeKey = TestNotificationProvider.ProviderTypeKeyValue,
                nonSecretConfig = new
                {
                    endpointBaseUrl = "https://provider.test",
                    apiToken = "super-secret-token-value",
                },
            });
        using var leakedResponse = await hostClient.SendAsync(leakedSecret, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, leakedResponse.StatusCode);
        var leakedBody = await leakedResponse.Content.ReadAsStringAsync(cancellationToken);
        AssertProblem(leakedBody, NotificationsErrorCodes.ProviderProfileValidationFailed);
        Assert.IsFalse(leakedBody.Contains("super-secret-token-value", StringComparison.Ordinal));

        var primaryKey = $"smtp-a-{Guid.NewGuid():N}"[..20];
        var secondaryKey = $"smtp-b-{Guid.NewGuid():N}"[..20];
        var primary = await CreateProfileAsync(hostClient, hostAdminToken, primaryKey, cancellationToken);
        var secondary = await CreateProfileAsync(hostClient, hostAdminToken, secondaryKey, cancellationToken);
        Assert.AreEqual(NotificationProfileCompilerStatus.NotConfigured, primary.SecretStatus);
        Assert.IsFalse(primary.IsEnabled);
        Assert.AreEqual(TestNotificationProvider.ProviderTypeKeyValue, primary.ProviderTypeKey);
        Assert.AreEqual(TestNotificationProvider.ProviderTypeKeyValue, secondary.ProviderTypeKey);

        using var secretUpdate = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/provider-profiles/{primary.Id:D}",
            hostAdminToken,
            new
            {
                nonSecretConfig = new { endpointBaseUrl = "https://provider.test" },
                secretReference = SecretReference,
                version = primary.Version,
            });
        using var secretUpdateResponse = await hostClient.SendAsync(secretUpdate, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secretUpdateResponse.StatusCode);
        var secretBody = await secretUpdateResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsFalse(secretBody.Contains(SecretReference, StringComparison.Ordinal));
        Assert.IsFalse(secretBody.Contains("secretReference", StringComparison.OrdinalIgnoreCase));
        var withSecret = JsonSerializer.Deserialize<NotificationProviderProfileResponse>(
            secretBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(withSecret);
        Assert.AreEqual(NotificationProfileCompilerStatus.Configured, withSecret.SecretStatus);

        using var configOnlyUpdate = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/provider-profiles/{primary.Id:D}",
            hostAdminToken,
            new
            {
                nonSecretConfig = new { endpointBaseUrl = "https://provider-v2.test" },
                secretReference = (string?)null,
                version = withSecret.Version,
            });
        using var configOnlyUpdateResponse = await hostClient.SendAsync(configOnlyUpdate, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, configOnlyUpdateResponse.StatusCode);
        var afterConfigOnlyUpdate = await configOnlyUpdateResponse.Content
            .ReadFromJsonAsync<NotificationProviderProfileResponse>(cancellationToken);
        Assert.IsNotNull(afterConfigOnlyUpdate);
        Assert.AreEqual(NotificationProfileCompilerStatus.Configured, afterConfigOnlyUpdate.SecretStatus);

        primary = await PublishAndEnableAsync(hostClient, hostAdminToken, afterConfigOnlyUpdate, cancellationToken);
        secondary = await PublishAndEnableAsync(hostClient, hostAdminToken, secondary, cancellationToken);
        Assert.IsTrue(primary.IsEnabled);
        Assert.IsTrue(secondary.IsEnabled);
        Assert.IsNotNull(primary.LatestPublishedVersionId);
        Assert.AreEqual(TestNotificationProvider.AdapterVersionValue, primary.LatestAdapterVersion);

        var producer = $"tests.notifications.{Guid.NewGuid():N}"[..28];
        var scene = "order.paid";
        var binding = await CreateAndPublishBindingAsync(
            hostClient,
            hostAdminToken,
            $"bind-{Guid.NewGuid():N}"[..20],
            producer,
            scene,
            primary.ProfileKey,
            cancellationToken);
        Assert.IsNotNull(binding.LatestPublishedVersionId);
        Assert.AreEqual(producer, binding.LatestProducerKey);
        Assert.AreEqual(TestNotificationProvider.ChannelKey, binding.LatestChannelKey);
        StringAssert.Contains(binding.LatestBindingTargetsJson, primary.LatestPublishedVersionId!.Value.ToString());

        using var stalePublish = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/bindings/{binding.Id:D}/publish",
            hostAdminToken,
            new PublishNotificationBindingRequest(binding.Version + 9));
        using var stalePublishResponse = await hostClient.SendAsync(stalePublish, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, stalePublishResponse.StatusCode);
        AssertProblem(
            await stalePublishResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.BindingConcurrencyConflict);

        using var duplicateScene = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/bindings",
            hostAdminToken,
            CreateBindingBody($"dup-{Guid.NewGuid():N}"[..16], producer, scene, primary.ProfileKey));
        using var duplicateCreateResponse = await hostClient.SendAsync(duplicateScene, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, duplicateCreateResponse.StatusCode);
        var duplicate = await duplicateCreateResponse.Content
            .ReadFromJsonAsync<NotificationBindingResponse>(cancellationToken);
        Assert.IsNotNull(duplicate);
        using var duplicatePublish = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/bindings/{duplicate.Id:D}/publish",
            hostAdminToken,
            new PublishNotificationBindingRequest(duplicate.Version));
        using var duplicatePublishResponse = await hostClient.SendAsync(duplicatePublish, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicatePublishResponse.StatusCode);
        AssertProblem(
            await duplicatePublishResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.BindingSceneConflict);

        var templateKey = $"tpl-{Guid.NewGuid():N}"[..20];
        await CreateAndPublishTestTemplateAsync(
            hostClient,
            hostAdminToken,
            templateKey,
            cancellationToken);
        using var intentRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            new
            {
                producerKey = producer,
                sceneKey = scene,
                templateKey,
                recipients = new[]
                {
                    new { recipientTypeKey = "user", recipientKey = hostUser.Id.ToString("N") },
                },
                parameters = new { orderNo = "B-1" },
                idempotencyKey = $"idem-{Guid.NewGuid():N}",
            });
        using var intentResponse = await hostClient.SendAsync(intentRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            intentResponse.StatusCode,
            await intentResponse.Content.ReadAsStringAsync(cancellationToken));
        var intent = await intentResponse.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken);
        Assert.IsNotNull(intent);
        Assert.AreEqual(binding.LatestPublishedVersionId, intent.BindingVersionId);
        Assert.AreEqual("single", intent.DispatchModeKey);
        StringAssert.Contains(intent.RouteSnapshotJson, primary.LatestPublishedVersionId!.Value.ToString());
        Assert.AreEqual(1, await CountDeliveriesAsync(factory, intent.Id, cancellationToken));
        Assert.AreEqual(0, await CountAttemptsForIntentAsync(factory, intent.Id, cancellationToken));

        using var disable = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/provider-profiles/{primary.Id:D}/disable",
            hostAdminToken,
            new SetNotificationProviderProfileEnabledRequest(primary.Version));
        using var disableResponse = await hostClient.SendAsync(disable, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var blockedIntent = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            new
            {
                producerKey = producer,
                sceneKey = scene,
                templateKey,
                recipients = new[]
                {
                    new { recipientTypeKey = "user", recipientKey = hostUser.Id.ToString("N") },
                },
                parameters = new { orderNo = "B-2" },
                idempotencyKey = $"idem-{Guid.NewGuid():N}",
            });
        using var blockedResponse = await hostClient.SendAsync(blockedIntent, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, blockedResponse.StatusCode);
        AssertProblem(
            await blockedResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.RouteProfileUnavailable);

        using var frozenGet = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/notifications/intents/{intent.Id:D}");
        frozenGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var frozenResponse = await hostClient.SendAsync(frozenGet, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, frozenResponse.StatusCode);
        var frozen = await frozenResponse.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken);
        Assert.IsNotNull(frozen);
        Assert.AreEqual(intent.BindingVersionId, frozen.BindingVersionId);

        var tenantToken = await EnterAcmeTenantAsync(tenantClient, tenantSwitchToken, cancellationToken);
        var tenantKey = $"ten-{Guid.NewGuid():N}"[..16];
        using var tenantCreate = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/provider-profiles",
            tenantToken,
            CreateProfileBody(tenantKey, tenantId: Guid.NewGuid()));
        using var tenantCreateResponse = await tenantClient.SendAsync(tenantCreate, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            tenantCreateResponse.StatusCode,
            await tenantCreateResponse.Content.ReadAsStringAsync(cancellationToken));
        var tenantProfile = await tenantCreateResponse.Content
            .ReadFromJsonAsync<NotificationProviderProfileResponse>(cancellationToken);
        Assert.IsNotNull(tenantProfile);
        using var hostGetTenant = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/notifications/provider-profiles/{tenantProfile.Id:D}");
        hostGetTenant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var hostGetTenantResponse = await hostClient.SendAsync(hostGetTenant, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, hostGetTenantResponse.StatusCode);

        using var crossBinding = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/bindings",
            tenantToken,
            CreateBindingBody($"tb-{Guid.NewGuid():N}"[..16], producer, "tenant.scene", primary.ProfileKey));
        using var crossBindingResponse = await tenantClient.SendAsync(crossBinding, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, crossBindingResponse.StatusCode);
        var tenantBinding = await crossBindingResponse.Content
            .ReadFromJsonAsync<NotificationBindingResponse>(cancellationToken);
        Assert.IsNotNull(tenantBinding);
        using var crossPublish = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/bindings/{tenantBinding.Id:D}/publish",
            tenantToken,
            new PublishNotificationBindingRequest(tenantBinding.Version));
        using var crossPublishResponse = await tenantClient.SendAsync(crossPublish, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, crossPublishResponse.StatusCode);
        AssertProblem(
            await crossPublishResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.ProviderProfileNotFound);

        await OpenApiNotificationsProfilesBindingsContractAssertions.VerifyAsync(
            hostClient,
            cancellationToken);
    }

    internal static async Task<NotificationProviderProfileResponse> CreateProfileAsync(
        HttpClient client,
        string token,
        string profileKey,
        CancellationToken cancellationToken)
        => await CreateProfileAsync(
            client,
            token,
            profileKey,
            TestNotificationProvider.ProviderTypeKeyValue,
            new { endpointBaseUrl = "https://provider.test" },
            secretReference: null,
            cancellationToken);

    internal static async Task<NotificationProviderProfileResponse> CreateProfileAsync(
        HttpClient client,
        string token,
        string profileKey,
        string providerTypeKey,
        object nonSecretConfig,
        string? secretReference,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/provider-profiles",
            token,
            new
            {
                profileKey,
                providerTypeKey,
                nonSecretConfig,
                secretReference,
            });
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var created = await response.Content.ReadFromJsonAsync<NotificationProviderProfileResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    internal static async Task<NotificationProviderProfileResponse> PublishAndEnableAsync(
        HttpClient client,
        string token,
        NotificationProviderProfileResponse profile,
        CancellationToken cancellationToken)
    {
        using var publish = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/provider-profiles/{profile.Id:D}/publish",
            token,
            new PublishNotificationProviderProfileRequest(profile.Version));
        using var publishResponse = await client.SendAsync(publish, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await publishResponse.Content
            .ReadFromJsonAsync<NotificationProviderProfileResponse>(cancellationToken);
        Assert.IsNotNull(published);
        using var enable = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/provider-profiles/{published.Id:D}/enable",
            token,
            new SetNotificationProviderProfileEnabledRequest(published.Version));
        using var enableResponse = await client.SendAsync(enable, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enableResponse.StatusCode);
        var enabled = await enableResponse.Content
            .ReadFromJsonAsync<NotificationProviderProfileResponse>(cancellationToken);
        Assert.IsNotNull(enabled);
        return enabled;
    }

    internal static async Task<NotificationBindingResponse> CreateAndPublishBindingAsync(
        HttpClient client,
        string token,
        string bindingKey,
        string producer,
        string scene,
        string profileKey,
        CancellationToken cancellationToken)
        => await CreateAndPublishBindingAsync(
            client,
            token,
            bindingKey,
            producer,
            scene,
            profileKey,
            TestNotificationProvider.ChannelKey,
            cancellationToken);

    internal static async Task<NotificationBindingResponse> CreateAndPublishBindingAsync(
        HttpClient client,
        string token,
        string bindingKey,
        string producer,
        string scene,
        string profileKey,
        string channelKey,
        CancellationToken cancellationToken)
    {
        using var create = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/bindings",
            token,
            new
            {
                bindingKey,
                dispatchModeKey = "single",
                producerKey = producer,
                sceneKey = scene,
                channelKey,
                targets = new[] { new { profileKey, order = 1 } },
            });
        using var createResponse = await client.SendAsync(create, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            createResponse.StatusCode,
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var created = await createResponse.Content.ReadFromJsonAsync<NotificationBindingResponse>(cancellationToken);
        Assert.IsNotNull(created);
        using var publish = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/bindings/{created.Id:D}/publish",
            token,
            new PublishNotificationBindingRequest(created.Version));
        using var publishResponse = await client.SendAsync(publish, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            publishResponse.StatusCode,
            await publishResponse.Content.ReadAsStringAsync(cancellationToken));
        var published = await publishResponse.Content.ReadFromJsonAsync<NotificationBindingResponse>(cancellationToken);
        Assert.IsNotNull(published);
        return published;
    }

    internal static async Task<NotificationTemplateResponse> CreateAndPublishTestTemplateAsync(
        HttpClient client,
        string token,
        string templateKey,
        CancellationToken cancellationToken)
        => await CreateAndPublishTestTemplateAsync(
            client,
            token,
            templateKey,
            TestNotificationProvider.ChannelKey,
            cancellationToken);

    internal static async Task<NotificationTemplateResponse> CreateAndPublishTestTemplateAsync(
        HttpClient client,
        string token,
        string templateKey,
        string channelKey,
        CancellationToken cancellationToken)
    {
        using var create = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/templates",
            token,
            new
            {
                templateKey,
                channelKey,
                contentCategoryKey = "transactional",
                draftSubject = "订单 {orderNo}",
                draftBody = new { text = "正文 {orderNo}" },
                parameterSchema = new
                {
                    schemaVersion = 1,
                    parameters = new[]
                    {
                        new { name = "orderNo", typeKey = "string", required = true, maxLength = 32 },
                    },
                },
            });
        using var createResponse = await client.SendAsync(create, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<NotificationTemplateResponse>(cancellationToken);
        Assert.IsNotNull(created);
        using var publish = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/templates/{created.Id:D}/publish",
            token,
            new PublishNotificationTemplateRequest(created.Version, "c1"));
        using var publishResponse = await client.SendAsync(publish, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await publishResponse.Content.ReadFromJsonAsync<NotificationTemplateResponse>(cancellationToken);
        Assert.IsNotNull(published);
        return published;
    }

    private static object CreateProfileBody(
        string profileKey,
        string providerTypeKey = TestNotificationProvider.ProviderTypeKeyValue,
        Guid? tenantId = null) =>
        new
        {
            profileKey,
            providerTypeKey,
            nonSecretConfig = new { endpointBaseUrl = "https://provider.test" },
            tenantId,
        };

    private static object CreateBindingBody(
        string bindingKey,
        string producerKey,
        string sceneKey,
        string profileKey) =>
        new
        {
            bindingKey,
            dispatchModeKey = "single",
            producerKey,
            sceneKey,
            channelKey = TestNotificationProvider.ChannelKey,
            targets = new[] { new { profileKey, order = 1 } },
        };

    private static async Task<long> CountAttemptsForIntentAsync(
        FullNetApiFactory factory,
        Guid intentId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        return await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountAttemptsByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", intentId)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<long> CountDeliveriesAsync(
        FullNetApiFactory factory,
        Guid intentId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        return await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountDeliveriesByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", intentId)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<string> EnterAcmeTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");
        using var enterRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            "/api/v1/tenancy/context",
            hostAccessToken,
            new ChangeTenantContextRequest(acme.Id));
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        return entered.AccessToken;
    }

    internal static async Task<CurrentUserResponse> GetCurrentUserAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        Assert.IsNotNull(currentUser);
        return currentUser;
    }

    internal static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    internal static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    internal static void AssertProblem(string body, string code)
    {
        using var document = JsonDocument.Parse(body);
        Assert.AreEqual(code, document.RootElement.GetProperty("code").GetString(), body);
    }

    private static class NotificationProfileCompilerStatus
    {
        public const string Configured = "configured";
        public const string NotConfigured = "not-configured";
    }
}
