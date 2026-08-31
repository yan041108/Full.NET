using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>模板 Draft/Publish、参数失败关闭、Intent 幂等与多收件人 Inbox 扇出验收。</summary>
internal static class NotificationTemplateIntentAssertions
{
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
            "/api/v1/notifications/templates",
            await factory.CreateHostAccessTokenAsync(
                [NotificationPlatformPermissions.TemplatesRead],
                cancellationToken),
            CreateTemplateBody($"deny-{Guid.NewGuid():N}"[..20], "订单 {orderNo}", "正文 {orderNo}"));
        using var forbiddenResponse = await hostClient.SendAsync(forbidden, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var emailTemplate = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/templates",
            hostAdminToken,
            CreateTemplateBody($"email-{Guid.NewGuid():N}"[..20], "主题", "正文", channelKey: "email"));
        using var emailResponse = await hostClient.SendAsync(emailTemplate, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, emailResponse.StatusCode);
        AssertProblem(
            await emailResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.IntentChannelUnsupported);

        var templateKey = $"order-paid-{Guid.NewGuid():N}"[..28];
        var created = await CreateTemplateAsync(
            hostClient,
            hostAdminToken,
            templateKey,
            cancellationToken);
        Assert.IsNull(created.LatestPublishedVersionId);

        using var staleUpdate = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/templates/{created.Id:D}",
            hostAdminToken,
            UpdateTemplateBody("订单 {orderNo}", "已更新 {orderNo}", created.Version + 9));
        using var staleUpdateResponse = await hostClient.SendAsync(staleUpdate, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleUpdateResponse.StatusCode);
        AssertProblem(
            await staleUpdateResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.TemplateConcurrencyConflict);

        using var update = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/templates/{created.Id:D}",
            hostAdminToken,
            UpdateTemplateBody("订单 {orderNo}", "已支付 {orderNo}", created.Version));
        using var updateResponse = await hostClient.SendAsync(update, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<NotificationTemplateResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);

        using var duplicate = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/templates",
            hostAdminToken,
            CreateTemplateBody(templateKey, "订单 {orderNo}", "正文 {orderNo}"));
        using var duplicateResponse = await hostClient.SendAsync(duplicate, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        AssertProblem(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.TemplateKeyConflict);

        using var unpublishedIntent = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            CreateIntentBody(
                templateKey,
                "tests.notifications",
                "order.paid",
                $"idem-{Guid.NewGuid():N}",
                [hostUser.Id],
                new { orderNo = "A-1" }));
        using var unpublishedIntentResponse = await hostClient.SendAsync(
            unpublishedIntent,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unpublishedIntentResponse.StatusCode);
        AssertProblem(
            await unpublishedIntentResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.TemplateNotPublished);

        var published = await PublishTemplateAsync(
            hostClient,
            hostAdminToken,
            created.Id,
            updated.Version,
            "c0",
            cancellationToken);
        Assert.IsNotNull(published.LatestPublishedVersionId);
        Assert.AreEqual(1, published.LatestPublishedVersionNumber);
        Assert.AreEqual(64, published.LatestContentHash?.Length);
        var firstVersionId = published.LatestPublishedVersionId!.Value;
        var firstHash = published.LatestContentHash!;

        var secondUser = await CreateInboxReaderAsync(hostClient, hostAdminToken, cancellationToken);
        var orderNo = $"A{Guid.NewGuid():N}"[..8];
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";
        using var firstIntent = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            CreateIntentBody(
                templateKey,
                "tests.notifications",
                "order.paid",
                idempotencyKey,
                [hostUser.Id, secondUser.Id],
                new { orderNo }));
        using var firstIntentResponse = await hostClient.SendAsync(firstIntent, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            firstIntentResponse.StatusCode,
            await firstIntentResponse.Content.ReadAsStringAsync(cancellationToken));
        var intent = await firstIntentResponse.Content.ReadFromJsonAsync<NotificationIntentResponse>(
            cancellationToken);
        Assert.IsNotNull(intent);
        Assert.AreEqual(firstVersionId, intent.TemplateVersionId);
        Assert.AreEqual("transactional", intent.PolicyCategoryKey);
        Assert.AreEqual("single", intent.DispatchModeKey);
        Assert.AreEqual("accepted", intent.StatusKey);
        Assert.AreEqual(2, intent.Recipients.Count);

        using var replay = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            CreateIntentBody(
                templateKey,
                "tests.notifications",
                "order.paid",
                idempotencyKey,
                [hostUser.Id, secondUser.Id],
                new { orderNo }));
        using var replayResponse = await hostClient.SendAsync(replay, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        var replayed = await replayResponse.Content.ReadFromJsonAsync<NotificationIntentResponse>(
            cancellationToken);
        Assert.IsNotNull(replayed);
        Assert.AreEqual(intent.Id, replayed.Id);

        using var conflict = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            CreateIntentBody(
                templateKey,
                "tests.notifications",
                "order.paid",
                idempotencyKey,
                [hostUser.Id, secondUser.Id],
                new { orderNo = "B-DIFF" }));
        using var conflictResponse = await hostClient.SendAsync(conflict, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        AssertProblem(
            await conflictResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.IntentIdempotencyConflict);

        const string secret = "SECRET-VALUE";
        using var unknownParam = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            CreateIntentBody(
                templateKey,
                "tests.notifications",
                "order.paid",
                $"idem-{Guid.NewGuid():N}",
                [hostUser.Id],
                new { orderNo, ssn = secret }));
        using var unknownParamResponse = await hostClient.SendAsync(unknownParam, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownParamResponse.StatusCode);
        var unknownBody = await unknownParamResponse.Content.ReadAsStringAsync(cancellationToken);
        AssertProblem(unknownBody, NotificationsErrorCodes.TemplateParameterInvalid);
        Assert.IsFalse(unknownBody.Contains(secret, StringComparison.Ordinal));

        using var missingParam = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            CreateIntentBody(
                templateKey,
                "tests.notifications",
                "order.paid",
                $"idem-{Guid.NewGuid():N}",
                [hostUser.Id],
                new { }));
        using var missingParamResponse = await hostClient.SendAsync(missingParam, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingParamResponse.StatusCode);
        AssertProblem(
            await missingParamResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.TemplateParameterInvalid);

        using var tooLong = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            hostAdminToken,
            CreateIntentBody(
                templateKey,
                "tests.notifications",
                "order.paid",
                $"idem-{Guid.NewGuid():N}",
                [hostUser.Id],
                new { orderNo = new string('x', 40) }));
        using var tooLongResponse = await hostClient.SendAsync(tooLong, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, tooLongResponse.StatusCode);
        AssertProblem(
            await tooLongResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.TemplateParameterInvalid);

        var hostInbox = await ListInboxAsync(hostClient, hostAdminToken, cancellationToken);
        Assert.AreEqual(
            1,
            hostInbox.Items.Count(item => item.Title == $"订单 {orderNo}"));
        var hostMessage = hostInbox.Items.Single(item => item.Title == $"订单 {orderNo}");
        Assert.AreEqual($"已支付 {orderNo}", hostMessage.Content);

        using var secondLogin = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(secondUser.Username, FullNetApiFactory.TestPassword)),
        };
        secondLogin.Headers.Add("Origin", "http://localhost");
        using var secondLoginResponse = await hostClient.SendAsync(secondLogin, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondLoginResponse.StatusCode);
        var secondToken = await secondLoginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(secondToken);
        var secondInbox = await ListInboxAsync(hostClient, secondToken.AccessToken, cancellationToken);
        Assert.AreEqual(1, secondInbox.Items.Count(item => item.Title == $"订单 {orderNo}"));

        using var updateForV2 = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/templates/{created.Id:D}",
            hostAdminToken,
            UpdateTemplateBody("订单 {orderNo}", "已发货 {orderNo}", published.Version));
        using var updateForV2Response = await hostClient.SendAsync(updateForV2, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateForV2Response.StatusCode);
        var draftV2 = await updateForV2Response.Content.ReadFromJsonAsync<NotificationTemplateResponse>(
            cancellationToken);
        Assert.IsNotNull(draftV2);
        var publishedV2 = await PublishTemplateAsync(
            hostClient,
            hostAdminToken,
            created.Id,
            draftV2.Version,
            "s2",
            cancellationToken);
        Assert.AreEqual(2, publishedV2.LatestPublishedVersionNumber);
        Assert.AreNotEqual(firstHash, publishedV2.LatestContentHash);
        using var getIntent = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/notifications/intents/{intent.Id:D}");
        getIntent.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var getIntentResponse = await hostClient.SendAsync(getIntent, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getIntentResponse.StatusCode);
        var frozen = await getIntentResponse.Content.ReadFromJsonAsync<NotificationIntentResponse>(
            cancellationToken);
        Assert.IsNotNull(frozen);
        Assert.AreEqual(firstVersionId, frozen.TemplateVersionId);

        var tenantToken = await EnterAcmeTenantAsync(tenantClient, tenantSwitchToken, cancellationToken);
        var tenantKey = $"tenant-{Guid.NewGuid():N}"[..20];
        using var tenantCreate = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/templates",
            tenantToken,
            new
            {
                templateKey = tenantKey,
                channelKey = "inbox",
                contentCategoryKey = "informational",
                draftSubject = "租户 {orderNo}",
                draftBody = new { text = "租户正文 {orderNo}" },
                parameterSchema = new
                {
                    schemaVersion = 1,
                    parameters = new[]
                    {
                        new { name = "orderNo", typeKey = "string", required = true, maxLength = 32 },
                    },
                },
                tenantId = Guid.NewGuid(),
            });
        using var tenantCreateResponse = await tenantClient.SendAsync(tenantCreate, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            tenantCreateResponse.StatusCode,
            await tenantCreateResponse.Content.ReadAsStringAsync(cancellationToken));
        var tenantTemplate = await tenantCreateResponse.Content
            .ReadFromJsonAsync<NotificationTemplateResponse>(cancellationToken);
        Assert.IsNotNull(tenantTemplate);
        using var hostGetTenant = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/notifications/templates/{tenantTemplate.Id:D}");
        hostGetTenant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var hostGetTenantResponse = await hostClient.SendAsync(hostGetTenant, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, hostGetTenantResponse.StatusCode);

        await OpenApiNotificationsTemplatesIntentsContractAssertions.VerifyAsync(
            hostClient,
            cancellationToken);
    }

    private static async Task<NotificationTemplateResponse> CreateTemplateAsync(
        HttpClient client,
        string token,
        string templateKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/templates",
            token,
            CreateTemplateBody(templateKey, "订单 {orderNo}", "正文 {orderNo}"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var created = await response.Content.ReadFromJsonAsync<NotificationTemplateResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task<NotificationTemplateResponse> PublishTemplateAsync(
        HttpClient client,
        string token,
        Guid templateId,
        long version,
        string classification,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/templates/{templateId:D}/publish",
            token,
            new PublishNotificationTemplateRequest(version, classification));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var published = await response.Content.ReadFromJsonAsync<NotificationTemplateResponse>(
            cancellationToken);
        Assert.IsNotNull(published);
        return published;
    }

    private static async Task<HostUserResponse> CreateInboxReaderAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var username = $"nintent-{Guid.NewGuid():N}"[..16];
        using var createRole = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest($"nrole-{Guid.NewGuid():N}"[..16], "意图收件角色"));
        using var createRoleResponse = await client.SendAsync(createRole, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var role = await createRoleResponse.Content.ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(role);
        using var assignPermissions = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{role.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest([InboxPermissions.Read], role.Version));
        using var assignPermissionsResponse = await client.SendAsync(assignPermissions, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignPermissionsResponse.StatusCode);
        var roleWithPermissions = await assignPermissionsResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(roleWithPermissions);

        using var createUser = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "意图收件用户", FullNetApiFactory.TestPassword));
        using var createUserResponse = await client.SendAsync(createUser, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUserResponse.StatusCode);
        var user = await createUserResponse.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(user);
        using var getRoles = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{user.Id:D}/roles");
        getRoles.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getRolesResponse = await client.SendAsync(getRoles, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getRolesResponse.StatusCode);
        var userRoles = await getRolesResponse.Content.ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(userRoles);
        using var assignRoles = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{user.Id:D}/roles",
            adminToken,
            new ReplaceHostUserRolesRequest([roleWithPermissions.Id], userRoles.Version));
        using var assignRolesResponse = await client.SendAsync(assignRoles, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignRolesResponse.StatusCode);
        return user;
    }

    private static object CreateTemplateBody(
        string templateKey,
        string subject,
        string body,
        string channelKey = "inbox") =>
        new
        {
            templateKey,
            channelKey,
            contentCategoryKey = "transactional",
            draftSubject = subject,
            draftBody = new { text = body },
            parameterSchema = new
            {
                schemaVersion = 1,
                parameters = new[]
                {
                    new { name = "orderNo", typeKey = "string", required = true, maxLength = 32 },
                },
            },
        };

    private static object UpdateTemplateBody(string subject, string body, long version) =>
        new
        {
            draftSubject = subject,
            draftBody = new { text = body },
            parameterSchema = new
            {
                schemaVersion = 1,
                parameters = new[]
                {
                    new { name = "orderNo", typeKey = "string", required = true, maxLength = 32 },
                },
            },
            version,
        };

    private static object CreateIntentBody(
        string templateKey,
        string producerKey,
        string sceneKey,
        string idempotencyKey,
        IReadOnlyList<Guid> recipientUserIds,
        object parameters) =>
        new
        {
            producerKey,
            sceneKey,
            templateKey,
            recipients = recipientUserIds
                .Select(id => new { recipientTypeKey = "user", recipientKey = id.ToString("N") })
                .ToArray(),
            parameters,
            idempotencyKey,
        };

    private static async Task<PagedInboxMessageResponses> ListInboxAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages?page=1&pageSize=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedInboxMessageResponses>(cancellationToken);
        Assert.IsNotNull(page);
        return page;
    }

    private static async Task<string> EnterAcmeTenantAsync(
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
        var entered = await enterResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>(
            cancellationToken);
        Assert.IsNotNull(entered);
        return entered.AccessToken;
    }

    private static async Task<CurrentUserResponse> GetCurrentUserAsync(
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

    private static async Task<string> LoginAsHostAdminAsync(
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

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
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

    private static void AssertProblem(string body, string code)
    {
        using var document = JsonDocument.Parse(body);
        Assert.AreEqual(code, document.RootElement.GetProperty("code").GetString(), body);
    }

    private sealed record PagedInboxMessageResponses(
        InboxMessageResponse[] Items,
        int Page,
        int PageSize,
        long Total);
}
