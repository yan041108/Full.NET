using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.IntegrationTests.Notifications;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>复用 HTTP 安排通知数据，并在原生 API 上验证新增治理契约；不调用真实厂商。</summary>
internal static class NativeNotificationGovernanceProbe
{
    internal static readonly IReadOnlyDictionary<string, string?> SmtpSettings = new Dictionary<string, string?>
    {
        ["Notifications:Providers:Smtp:Enabled"] = "true",
        ["Notifications:SecretReferences:email.smtp:0"] = "env://FULLNET_NATIVE_NOTIFICATIONS_UNCONFIGURED_SECRET",
    };

    internal static async Task VerifyApiAsync(HttpClient client, string token, DatabaseProvider provider,
        string connectionString, CancellationToken ct)
    {
        const string root = "/api/v1/notifications";
        using (var anonymous = await client.GetAsync(root + "/quotas", ct))
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, ct);
        var preference = await CallAsync<NotificationPreferenceResponse>(client, token, HttpMethod.Put,
            root + "/my-preferences/email", new UpdateMyNotificationPreferenceRequest("email", true, false,
                """{"timeZone":"Asia/Shanghai","start":"22:00","end":"08:00"}"""), HttpStatusCode.OK, ct);
        Assert.IsTrue(preference.ChannelOptedOut);
        Assert.IsFalse(preference.MarketingConsentGranted);
        Assert.IsTrue(preference.Version > 0);
        using (var invalid = await SendAsync(client, token, HttpMethod.Put, root + "/my-preferences/email",
            new UpdateMyNotificationPreferenceRequest("email", false, true,
                """{"timeZone":"Invalid/Zone","start":"22:00","end":"08:00"}"""), ct))
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        var prefs = await CallAsync<NotificationPreferenceResponse[]>(client, token, HttpMethod.Get,
            root + "/my-preferences", null, HttpStatusCode.OK, ct);
        Assert.IsTrue(prefs.Any(p => p.ChannelKey == "email" && p.Version == preference.Version));

        var producer = "native." + Guid.NewGuid().ToString("N")[..16];
        var scene = await CallAsync<NotificationProducerSceneResponse>(client, token, HttpMethod.Post,
            root + "/producer-scenes", new CreateNotificationProducerSceneRequest(producer, "notice", "transactional", ["inbox"], false),
            HttpStatusCode.Created, ct);
        using (var stale = await SendAsync(client, token, HttpMethod.Put, root + $"/producer-scenes/{scene.Id:D}",
            new UpdateNotificationProducerSceneRequest("transactional", ["inbox"], false, scene.Version + 1), ct))
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        var quota = await CallAsync<NotificationQuotaPolicyResponse>(client, token, HttpMethod.Post, root + "/quotas",
            new CreateNotificationQuotaPolicyRequest("producer", producer, 60, 2), HttpStatusCode.Created, ct);
        quota = await CallAsync<NotificationQuotaPolicyResponse>(client, token, HttpMethod.Put, root + $"/quotas/{quota.Id:D}",
            new UpdateNotificationQuotaPolicyRequest(60, 3, quota.Version), HttpStatusCode.OK, ct);
        Assert.AreEqual(3, quota.MaxCount);
        quota = await CallAsync<NotificationQuotaPolicyResponse>(client, token, HttpMethod.Post,
            root + $"/quotas/{quota.Id:D}/enabled", new SetNotificationQuotaPolicyEnabledRequest(false, quota.Version), HttpStatusCode.OK, ct);
        Assert.IsFalse(quota.IsEnabled);
        _ = await CallAsync<PagedResult<NotificationQuotaPolicyResponse>>(client, token, HttpMethod.Get,
            root + "/quotas", null, HttpStatusCode.OK, ct);
        _ = await CallAsync<JsonElement>(client, token, HttpMethod.Get, root + "/quotas/outstanding", null, HttpStatusCode.OK, ct);
        _ = await CallAsync<JsonElement>(client, token, HttpMethod.Get, root + "/deliveries/summary", null, HttpStatusCode.OK, ct);

        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, false));
        await db.OpenAsync(ct);
        await CallAsync<NotificationPreferenceResponse>(client, token, HttpMethod.Put, root + "/my-preferences/email",
            new UpdateMyNotificationPreferenceRequest("email", false, true, null), HttpStatusCode.OK, ct);
        var emailRoute = await CreateEmailRouteAsync(client, token, "transactional", ct);
        var emailIntent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, token, emailRoute, user.Id, "native-reconcile", ct);
        var delivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, emailIntent.Id, ct);
        // 模拟需要人工核对的存量事实；实际状态更新、额度收敛和 B0 审计由原生 HTTP 事务完成。
        await db.ExecuteAsync(new CommandDefinition("UPDATE fn_notifications_delivery SET StatusKey = 'unknown' WHERE Id = @Id",
            new { delivery.Id }, cancellationToken: ct));
        using (var stale = await SendAsync(client, token, HttpMethod.Post, root + $"/deliveries/{delivery.Id:D}/reconcile",
            new ReconcileNotificationDeliveryRequest(delivery.Revision + 1, "confirmed_not_called", "native-test://fixture"), ct))
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        delivery = await CallAsync<NotificationDeliveryResponse>(client, token, HttpMethod.Post,
            root + $"/deliveries/{delivery.Id:D}/reconcile",
            new ReconcileNotificationDeliveryRequest(delivery.Revision, "confirmed_not_called", "native-test://fixture"), HttpStatusCode.OK, ct);
        Assert.AreEqual("failed", delivery.StatusKey);
        var summary = await CallAsync<NotificationDeliverySummaryResponse[]>(client, token, HttpMethod.Get,
            root + "/deliveries/summary", null, HttpStatusCode.OK, ct);
        Assert.IsTrue(summary.Any(row => row.ProviderTypeKey == "email.smtp" && row.StatusKey == "failed"
            && row.Count == 1 && row.ReceiptModeKey == "none"));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM fn_notifications_domain_audit WHERE ResourceId = @Id AND OperationKey = 'delivery.reconcile'",
            new { delivery.Id }, cancellationToken: ct)));
        await VerifyMiniSubscriptionsAsync(client, token, user.Id, provider, db, ct);

        var template = "native-" + Guid.NewGuid().ToString("N")[..16];
        await NotificationProfileBindingAssertions.CreateAndPublishTestTemplateAsync(client, token, template, "inbox", ct);
        var body = new CreateNotificationIntentRequest(producer, "notice", template, [new("user", user.Id.ToString("N"))],
            JsonSerializer.SerializeToElement(new { orderNo = "native" }), Guid.NewGuid().ToString("N"),
            DeepLink: new NotificationDeepLink("workflow_todo", Guid.CreateVersion7()));
        var intent = await CallAsync<NotificationIntentResponse>(client, token, HttpMethod.Post, root + "/intents", body, HttpStatusCode.Created, ct);
        var list = await CallAsync<PagedResult<InboxMessageResponse>>(client, token, HttpMethod.Get,
            root + $"/my-inbox-messages?producerKey={producer}&sceneKey=notice", null, HttpStatusCode.OK, ct);
        var message = list.Items.Single();
        Assert.AreEqual(body.DeepLink, message.DeepLink);
        var before = await CallAsync<InboxUnreadCountResponse>(client, token, HttpMethod.Get,
            root + "/my-inbox-messages/unread-count", null, HttpStatusCode.OK, ct);
        var archived = await CallAsync<InboxUnreadCountResponse>(client, token, HttpMethod.Post,
            root + "/my-inbox-messages/archive-batch", new ArchiveInboxMessagesRequest([message.Id]), HttpStatusCode.OK, ct);
        Assert.AreEqual(before.UnreadCount - 1, archived.UnreadCount);
        var repeated = await CallAsync<InboxUnreadCountResponse>(client, token, HttpMethod.Post,
            root + $"/my-inbox-messages/{message.Id:D}/archive", null, HttpStatusCode.OK, ct);
        Assert.AreEqual(archived.UnreadCount, repeated.UnreadCount);
        using (var stale = await SendAsync(client, token, HttpMethod.Post, root + $"/intents/{intent.Id:D}/revoke",
            new RevokeNotificationIntentRequest(intent.Revision + 1, "native-cancel"), ct))
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        var revoked = await CallAsync<NotificationRevocationResponse>(client, token, HttpMethod.Post,
            root + $"/intents/{intent.Id:D}/revoke", new RevokeNotificationIntentRequest(intent.Revision, "native-cancel"), HttpStatusCode.OK, ct);
        Assert.AreEqual(1, revoked.RevokedInboxMessages);
        Assert.AreEqual(0, revoked.CrossedSendBoundary);
        list = await CallAsync<PagedResult<InboxMessageResponse>>(client, token, HttpMethod.Get,
            root + $"/my-inbox-messages?archived=true&producerKey={producer}", null, HttpStatusCode.OK, ct);
        var withdrawn = list.Items.Single();
        Assert.IsNotNull(withdrawn.RevokedAtUtc);
        Assert.IsNotNull(withdrawn.ArchivedAtUtc);
        Assert.AreEqual("unread", withdrawn.Status);
        Assert.AreEqual(string.Empty, withdrawn.Content);
        Assert.IsNull(withdrawn.DeepLink);
        // Host 的受控目录和站内信不可被租户会话读到。
        var tenantToken = await NativeApiE2EAssertions.EnterLocalTenantAsync(client, token, ct);
        var tenantList = await CallAsync<PagedResult<InboxMessageResponse>>(client, tenantToken, HttpMethod.Get,
            root + $"/my-inbox-messages?archived=true&producerKey={producer}", null, HttpStatusCode.OK, ct);
        Assert.AreEqual(0, tenantList.Items.Count);
        using var crossScope = await SendAsync(client, tenantToken, HttpMethod.Post, root + $"/intents/{intent.Id:D}/revoke",
            new RevokeNotificationIntentRequest(intent.Revision, "cross-scope"), ct);
        Assert.AreEqual(HttpStatusCode.NotFound, crossScope.StatusCode);
    }

    internal static async Task<NotificationDeliveryWorkerAssertions.RouteFixture> CreateEmailRouteAsync(
        HttpClient client, string token, string category, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(client, token, "native-" + suffix,
            "email.smtp", new { host = "smtp.example.invalid", port = 465, secureSocketMode = "ssl_on_connect",
                username = "native@example.invalid", fromAddress = "native@example.invalid", fromDisplayName = "原生验收" },
            "env://FULLNET_NATIVE_NOTIFICATIONS_UNCONFIGURED_SECRET", ct);
        profile = await NotificationProfileBindingAssertions.PublishAndEnableAsync(client, token, profile, ct);
        var producer = "native.email." + suffix;
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(client, token, producer, "notice", "email", ct, category);
        await NotificationProfileBindingAssertions.CreateAndPublishBindingAsync(client, token, "native-" + suffix,
            producer, "notice", profile.ProfileKey, "email", ct);
        await NotificationProfileBindingAssertions.CreateAndPublishTestTemplateAsync(client, token, "native-" + suffix, "email", ct);
        return new(producer, "notice", "native-" + suffix);
    }

    private static async Task VerifyMiniSubscriptionsAsync(HttpClient client, string token, Guid userId,
        DatabaseProvider provider, DbConnection db, CancellationToken ct)
    {
        var appId = "wx" + Guid.NewGuid().ToString("N")[..24];
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(client, token, "native-" + appId,
            "im.wechat_miniprogram", new { appId, defaultTemplateId = "native-template" },
            "env://FULLNET_NATIVE_MINI_UNCONFIGURED_SECRET", ct);
        profile = await NotificationProfileBindingAssertions.PublishAndEnableAsync(client, token, profile, ct);
        var bindingId = Guid.CreateVersion7();
        // 预置已完成身份交换的隔离事实；这里只验本地订阅台账，绝不据此宣称厂商授权或库存。
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_notifications_wechat_miniprogram_binding
                (Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected,
                 OpenIdMask, OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey,
                 RecipientEndpointId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@Id, 'host', @UserId, @AppId, @ProfileId, 'fixture-not-decrypted',
                 'nat***', @Hash, NULL, NULL, 'verified', NULL, @Now, NULL)
            """, new { Id = bindingId, UserId = userId, AppId = appId, ProfileId = profile.LatestPublishedVersionId!.Value,
                Hash = new string('0', 64), Now = provider == DatabaseProvider.MySql ? (object)DateTime.UtcNow : DateTimeOffset.UtcNow }, cancellationToken: ct));
        var path = $"/api/v1/notifications/wechat-miniprogram/bindings/{appId}/subscriptions";
        var accepted = new RecordWeChatMiniProgramSubscriptionRequest("native-template", "accepted", Guid.CreateVersion7());
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var report = await CallAsync<WeChatMiniProgramBindingResponse>(client, token, HttpMethod.Post,
                path, accepted, HttpStatusCode.OK, ct);
            Assert.AreEqual("accepted", report.Subscriptions.Single().StatusKey);
        }
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM fn_notifications_subscription_grant WHERE BindingId = @Id AND StateKey = 'available'",
            new { Id = bindingId }, cancellationToken: ct)));
        _ = await CallAsync<WeChatMiniProgramBindingResponse>(client, token, HttpMethod.Post, path,
            accepted with { StatusKey = "rejected", AuthorizationId = Guid.CreateVersion7() }, HttpStatusCode.OK, ct);
        var replay = await CallAsync<WeChatMiniProgramBindingResponse>(client, token, HttpMethod.Post,
            path, accepted, HttpStatusCode.OK, ct);
        Assert.AreEqual("rejected", replay.Subscriptions.Single().StatusKey);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM fn_notifications_subscription_grant WHERE BindingId = @Id AND StateKey = 'available'",
            new { Id = bindingId }, cancellationToken: ct)));
        _ = await CallAsync<WeChatMiniProgramBindingResponse[]>(client, token, HttpMethod.Get,
            "/api/v1/notifications/wechat-miniprogram/bindings/mine", null, HttpStatusCode.OK, ct);
    }

    internal static async Task<T> CallAsync<T>(HttpClient client, string token, HttpMethod method, string path,
        object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await SendAsync(client, token, method, path, body, ct);
        Assert.AreEqual(expected, response.StatusCode, path + "\n" + await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<T>(ct);
        Assert.IsNotNull(value);
        return value;
    }

    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method,
        string path, object? body, CancellationToken ct)
    {
        using var request = NotificationProfileBindingAssertions.CreateBearerJsonRequest(method, path, token, body);
        return await client.SendAsync(request, ct);
    }
}
