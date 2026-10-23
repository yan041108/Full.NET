using System.Data.Common;
using System.Net;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using static Full.NET.IntegrationTests.NativeAot.NativeNotificationGovernanceProbe;
using static Full.NET.IntegrationTests.Notifications.NotificationDeliveryWorkerAssertions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>JIT API 只安排隔离数据；所有领取、治理、预留与恢复均由独立原生 Worker 执行。</summary>
internal static class NativeWorkerNotificationGovernanceProbe
{
    internal static async Task VerifyAsync(DatabaseProvider provider, string connectionString, CancellationToken ct)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Inconclusive(reason ?? "未找到原生 Worker 产物。");
        var keys = Path.Combine(Path.GetTempPath(), "fullnet-native-notification-keys-" + Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?>(SmtpSettings)
        {
            ["DataProtection:KeyRingPath"] = keys,
            ["Notifications:Worker:PollMilliseconds"] = "100",
            ["Notifications:Worker:MaxAttempts"] = "2",
            ["Notifications:Worker:BacklogSampleSeconds"] = "5",
        };
        await using var factory = new FullNetApiFactory(provider, connectionString, settings);
        await factory.InitializeAsync(ct);
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, ct);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, ct);
        await PreferenceAsync(true, null);
        Assert.IsTrue(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FULLNET_NATIVE_NOTIFICATIONS_UNCONFIGURED_SECRET")));
        var marketing = await CreateEmailRouteAsync(client, token, "marketing", ct);
        var transactional = await CreateEmailRouteAsync(client, token, "transactional", ct);
        var consentIntent = await CreateIntentAsync(client, token, marketing, user.Id, "withdrawal", ct);
        var quietIntent = await CreateIntentAsync(client, token, transactional, user.Id, "quiet", ct);
        var secondIntent = await CreateIntentAsync(client, token, transactional, user.Id, "quota-release", ct);
        var consent = await WaitDeliveryAsync(client, token, consentIntent.Id, ct);
        var quiet = await WaitDeliveryAsync(client, token, quietIntent.Id, ct);
        var second = await WaitDeliveryAsync(client, token, secondIntent.Id, ct);
        Assert.AreEqual("accepted", consent.StatusKey);
        Assert.AreEqual("accepted", quiet.StatusKey);
        var quota = await CallAsync<NotificationQuotaPolicyResponse>(client, token, HttpMethod.Post,
            "/api/v1/notifications/quotas", new CreateNotificationQuotaPolicyRequest("producer", transactional.Producer, 86400, 1),
            HttpStatusCode.Created, ct);
        Assert.IsTrue(quota.IsEnabled);
        var endpoint = await CallAsync<RecipientEndpointResponse>(client, token, HttpMethod.Post,
            "/api/v1/notifications/my-recipient-endpoints", new CreateMyRecipientEndpointRequest(
                quiet.ProviderProfileVersionId!.Value, "email", "native@example.invalid"), HttpStatusCode.Created, ct);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, false));
        await db.OpenAsync(ct);
        // 只标记测试自有端点，不发送验证码；缺少测试 Secret 保证 SMTP 在网络调用前确定失败。
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE fn_notifications_recipient_endpoint SET VerificationStatusKey = 'verified' WHERE Id = @Id",
            new { endpoint.Id }, cancellationToken: ct));
        var snapshot = await db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT ContentSnapshotJson FROM fn_notifications_delivery WHERE Id = @Id", new { quiet.Id }, cancellationToken: ct));
        var now = DateTimeOffset.UtcNow;
        var quietHours = System.Text.Json.JsonSerializer.Serialize(new
        {
            timeZone = "UTC", start = now.AddHours(-1).ToString("HH:mm"), end = now.AddHours(1).ToString("HH:mm"),
        });
        await PreferenceAsync(false, quietHours);
        await using var host = await NativeWorkerProcessHost.StartAsync(artifact, provider, connectionString,
            NativeAotTestTimeouts.ProcessStartup, ct, additionalSettings: settings);
        consent = await WaitAsync(consent.Id, d => d.StatusKey == "suppressed");
        Assert.AreEqual(NotificationsErrorCodes.PolicyMarketingConsentRequired, consent.SuppressionReasonCode);
        quiet = await WaitAsync(quiet.Id, d => d.NextAttemptAtUtc > DateTimeOffset.UtcNow.AddMinutes(30));
        second = await WaitAsync(second.Id, d => d.NextAttemptAtUtc > DateTimeOffset.UtcNow.AddMinutes(30));
        Assert.AreEqual("accepted", quiet.StatusKey);
        Assert.AreEqual(0, quiet.Attempts.Count);
        Assert.AreEqual(0L, await ScalarAsync("SELECT AttemptBudgetUsed FROM fn_notifications_delivery WHERE Id = @Id", quiet.Id));
        await PreferenceAsync(false, null);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE fn_notifications_delivery SET NextAttemptAtUtc = NULL WHERE Id IN (@First, @Second)",
            new { First = quiet.Id, Second = second.Id }, cancellationToken: ct));
        quiet = await WaitAsync(quiet.Id, d => d.StatusKey == "failed");
        second = await WaitAsync(second.Id, d => d.StatusKey == "failed");
        Assert.AreEqual(1, quiet.Attempts.Count);
        Assert.AreEqual(1, second.Attempts.Count);
        foreach (var delivery in new[] { quiet, second })
            Assert.AreEqual(4L, await ScalarAsync(
                "SELECT COUNT(*) FROM fn_notifications_quota_reservation WHERE DeliveryId = @Id AND StateKey = 'released'", delivery.Id));
        Assert.AreEqual(snapshot, await db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT ContentSnapshotJson FROM fn_notifications_delivery WHERE Id = @Id", new { quiet.Id }, cancellationToken: ct)));

        // 模拟调用后中断和预算耗尽的过期租约；原生进程必须保留 Unknown，且不能无限重新领取。
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE fn_notifications_delivery SET StatusKey = 'unknown', NextAttemptAtUtc = NULL,
                LeaseExpiresAtUtc = NULL, LeaseOwnerKey = NULL, Revision = Revision + 1 WHERE Id = @Id
            """, new { quiet.Id }, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE fn_notifications_delivery SET StatusKey = 'accepted', AttemptBudgetUsed = 2,
                NextAttemptAtUtc = NULL, LeaseOwnerKey = 'interrupted-native-worker', LeaseExpiresAtUtc = @Past,
                Revision = Revision + 1 WHERE Id = @Id
            """, new { second.Id, Past = provider == DatabaseProvider.MySql ? (object)DateTime.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddMinutes(-1) }, cancellationToken: ct));
        second = await WaitAsync(second.Id, d => d.StatusKey == "dead_lettered");
        Assert.AreEqual(2, second.Attempts.Count);
        quiet = await GetDeliveryAsync(client, token, quiet.Id, ct);
        Assert.AreEqual("unknown", quiet.StatusKey);
        Assert.AreEqual(1, quiet.Attempts.Count);
        _ = await CallAsync<NotificationDeliveryResponse>(client, token, HttpMethod.Post,
            $"/api/v1/notifications/deliveries/{quiet.Id:D}/reconcile",
            new ReconcileNotificationDeliveryRequest(quiet.Revision, "confirmed_not_called", "native-test://missing-secret"), HttpStatusCode.OK, ct);
        await host.StopGracefullyAsync(TimeSpan.FromSeconds(30), ct);
        Assert.AreEqual(0, host.ExitCode, host.LogFilePath);
        host.AssertNoFatalMarkersInLogs();

        Task<NotificationPreferenceResponse> PreferenceAsync(bool consentGranted, string? quietJson) =>
            CallAsync<NotificationPreferenceResponse>(client, token, HttpMethod.Put, "/api/v1/notifications/my-preferences/email",
                new UpdateMyNotificationPreferenceRequest("email", false, consentGranted, quietJson), HttpStatusCode.OK, ct);
        async Task<long> ScalarAsync(string sql, Guid id) => await db.QuerySingleAsync<long>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        async Task<NotificationDeliveryResponse> WaitAsync(Guid id, Func<NotificationDeliveryResponse, bool> predicate)
        {
            var until = DateTime.UtcNow.AddSeconds(60);
            do
            {
                var delivery = await GetDeliveryAsync(client, token, id, ct);
                if (predicate(delivery)) return delivery;
                await Task.Delay(200, ct);
            } while (DateTime.UtcNow < until);
            throw new TimeoutException($"原生通知治理未达到预期：{id:D}；日志：{host.LogFilePath}");
        }
    }
}
