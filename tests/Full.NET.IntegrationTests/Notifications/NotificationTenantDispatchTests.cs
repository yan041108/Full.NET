using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>验证企业暂停对已领取的外发通知生效，并保留原投递身份。</summary>
[TestClass]
public sealed class NotificationTenantDispatchTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Suspending_tenant_blocks_provider_and_reactivation_resumes_original_delivery(DatabaseProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = timeout.Token;
        var connectionString = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var clock = new OffsetClock();
        using var factory = new FullNetApiFactory(provider, connectionString,
            settingsOverrides: new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" },
            configureTestServices: services =>
            {
                NotificationProfileBindingAssertions.ConfigureTestServices(services);
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            });
        await factory.InitializeAsync(cancellationToken, useSchemaTemplate: false);
        using var host = factory.CreateClientForHost("localhost");
        using var tenant = factory.CreateClientForHost("localhost");
        var hostToken = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(host, cancellationToken);
        var tenantEntry = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(tenant, cancellationToken);
        var tenantToken = await NotificationProfileBindingAssertions.EnterAcmeTenantAsync(tenant, tenantEntry, cancellationToken);
        var tenantUser = await NotificationProfileBindingAssertions.GetCurrentUserAsync(tenant, tenantToken, cancellationToken);
        Assert.IsNotNull(tenantUser.TenantId);
        var route = await NotificationDeliveryWorkerAssertions.CreateRouteAsync(tenant, tenantToken, tenantUser.Id, cancellationToken);
        var intent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(
            tenant, tenantToken, route, tenantUser.Id, "tenant-suspend", cancellationToken);
        var delivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(tenant, tenantToken, intent.Id, cancellationToken);
        using var expiringRequest = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post, "/api/v1/notifications/intents", tenantToken,
            new CreateNotificationIntentRequest(route.Producer, route.Scene, route.TemplateKey,
                [new NotificationRecipientInput("user", tenantUser.Id.ToString("N"))],
                JsonSerializer.SerializeToElement(new { orderNo = "expires-during-pause" }),
                $"expires-{Guid.CreateVersion7():N}", ExpiresAtUtc: clock.UtcNow.AddMinutes(2)));
        using var expiringResponse = await tenant.SendAsync(expiringRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, expiringResponse.StatusCode,
            await expiringResponse.Content.ReadAsStringAsync(cancellationToken));
        var expiring = await expiringResponse.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken);
        Assert.IsNotNull(expiring);
        var expiringDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(tenant, tenantToken, expiring.Id, cancellationToken);

        var hostUser = await NotificationProfileBindingAssertions.GetCurrentUserAsync(host, hostToken, cancellationToken);
        var hostRoute = await NotificationDeliveryWorkerAssertions.CreateRouteAsync(host, hostToken, hostUser.Id, cancellationToken);
        var hostIntent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(
            host, hostToken, hostRoute, hostUser.Id, "host-during-tenant-suspend", cancellationToken);
        var hostDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(host, hostToken, hostIntent.Id, cancellationToken);

        // 正式暂停规则保护最后一家活动企业；使用同一供给入口创建独立的活动企业。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var key = $"dispatch-{Guid.CreateVersion7():N}";
            var created = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(new(key, "Other active tenant", $"{key}.localhost"), cancellationToken);
            Assert.IsTrue(created.IsSuccess, created.Error?.Message);
        }
        host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var path = $"/api/v1/tenancy/tenants/{tenantUser.TenantId.Value:D}";
        var active = await host.GetFromJsonAsync<TenantSummary>(path + "/lifecycle", cancellationToken);
        Assert.IsNotNull(active);
        using var pause = await host.PostAsJsonAsync(path + "/suspend", new { active.Version }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, pause.StatusCode, await pause.Content.ReadAsStringAsync(cancellationToken));
        var paused = await pause.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(paused);
        Assert.IsFalse(paused.IsActive);

        var harness = factory.Services.GetRequiredService<TestNotificationProviderHarness>();
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        Assert.HasCount(1, harness.IdempotencyKeysFor(hostDelivery.Id));
        Assert.HasCount(0, harness.IdempotencyKeysFor(delivery.Id));
        Assert.HasCount(0, harness.IdempotencyKeysFor(expiringDelivery.Id));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var deferred = await queries.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("tests.notifications.tenant_pause_budget",
                    """
                    SELECT COUNT(1) FROM fn_notifications_delivery
                    WHERE Id = @Id AND StatusKey = 'accepted' AND AttemptBudgetUsed = 0
                      AND SuppressionReasonCode = @Reason AND NextAttemptAtUtc > @Now
                    """, SqlDataScope.Global),
                new Dictionary<string, object?>
                {
                    ["Id"] = delivery.Id, ["Reason"] = NotificationDeliveryBatchProcessor.TenantInactive, ["Now"] = clock.UtcNow
                }, cancellationToken);
            Assert.AreEqual(1L, deferred);
        }

        using var resume = await host.PostAsJsonAsync(path + "/reactivate", new ReactivateTenantRequest(paused.Version), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode, await resume.Content.ReadAsStringAsync(cancellationToken));
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        Assert.HasCount(0, harness.IdempotencyKeysFor(delivery.Id));
        clock.Offset = TimeSpan.FromMinutes(6);
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        Assert.HasCount(1, harness.IdempotencyKeysFor(delivery.Id));
        Assert.HasCount(1, harness.IdempotencyKeysFor(hostDelivery.Id));
        Assert.HasCount(0, harness.IdempotencyKeysFor(expiringDelivery.Id));
        // 只推进 Worker 的重试与过期窗口，重新签发 JWT 前恢复宿主时钟。
        clock.Offset = TimeSpan.Zero;
        tenantEntry = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(tenant, cancellationToken);
        tenantToken = await NotificationProfileBindingAssertions.EnterAcmeTenantAsync(tenant, tenantEntry, cancellationToken);
        var resumed = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(tenant, tenantToken, delivery.Id, cancellationToken);
        Assert.AreEqual(intent.Id, resumed.IntentId);
        Assert.AreEqual("sent", resumed.StatusKey);
        Assert.IsNull(resumed.SuppressionReasonCode);
        var expired = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(tenant, tenantToken, expiringDelivery.Id, cancellationToken);
        Assert.AreEqual("suppressed", expired.StatusKey);
        Assert.AreEqual("notifications.message_expired", expired.SuppressionReasonCode);
    }

    private sealed class OffsetClock : IClock
    {
        public TimeSpan Offset { get; set; }
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Offset;
    }
}
