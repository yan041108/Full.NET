using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>订阅状态管理贯穿真实 HTTP、租户 SQL、事件扇出与 Worker 领取，不发送外部 HTTP。</summary>
[TestClass]
public sealed class WebhookSubscriptionConsoleTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Tenant_status_confirmation_preserves_history_and_controls_fanout_and_claims(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(token);
        using var host = factory.CreateClientForHost("localhost"); using var a = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(a, token);
        var domain = "webhook-b-" + Guid.CreateVersion7().ToString("N") + ".localhost"; TenantSummary bTenant;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var result = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(new("webhook-b-" + Guid.CreateVersion7().ToString("N"), "Webhook B", domain), token);
            Assert.IsTrue(result.IsSuccess); bTenant = result.Value!;
        }
        using var b = factory.CreateClientForHost(domain);
        a.DefaultRequestHeaders.Authorization = new("Bearer", await AccessAsync(tenant.Id, [WebhookPermissions.SubscriptionsRead, WebhookPermissions.SubscriptionsManage, WebhookPermissions.SubscriptionsSetStatus]));
        b.DefaultRequestHeaders.Authorization = new("Bearer", await AccessAsync(bTenant.Id, [WebhookPermissions.SubscriptionsRead, WebhookPermissions.SubscriptionsSetStatus]));
        const string secret = "integration-webhook-independent-secret";
        using var createdResponse = await a.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new CreateWebhookSubscriptionRequest("fullnet.workflow.instance.completed", "https://example.com/hooks/console", secret), token);
        Assert.AreEqual(HttpStatusCode.OK, createdResponse.StatusCode);
        var createdBody = await createdResponse.Content.ReadAsStringAsync(token); Assert.IsFalse(createdBody.Contains(secret, StringComparison.Ordinal));
        var subscription = await createdResponse.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token); Assert.IsNotNull(subscription);
        var path = $"/api/v1/webhooks/subscriptions/{subscription.Id:D}/status";
        using (var foreign = await b.PostAsJsonAsync(path, new SetWebhookSubscriptionStatusRequest(false, subscription.Version), token))
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        foreach (var permissions in new[] { new[] { WebhookPermissions.SubscriptionsRead }, new[] { WebhookPermissions.SubscriptionsSetStatus } })
        {
            using var limited = factory.CreateClientForHost("acme.localhost"); limited.DefaultRequestHeaders.Authorization = new("Bearer", await AccessAsync(tenant.Id, permissions));
            using var denied = await limited.PostAsJsonAsync(path, new SetWebhookSubscriptionStatusRequest(false, subscription.Version), token); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var anonymous = factory.CreateClientForHost("acme.localhost"))
        using (var denied = await anonymous.PostAsJsonAsync(path, new SetWebhookSubscriptionStatusRequest(false, subscription.Version), token)) Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        var originalEvent = Guid.CreateVersion7(); await EnqueueAsync(originalEvent);
        Assert.AreEqual(1, await CountAsync(originalEvent));
        using var pausedResponse = await a.PostAsJsonAsync(path, new SetWebhookSubscriptionStatusRequest(false, subscription.Version), token);
        Assert.AreEqual(HttpStatusCode.OK, pausedResponse.StatusCode);
        var paused = await pausedResponse.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token); Assert.IsNotNull(paused); Assert.IsFalse(paused.IsActive); Assert.AreEqual(subscription.Version + 1, paused.Version);
        using (var repeated = await a.PostAsJsonAsync(path, new SetWebhookSubscriptionStatusRequest(false, paused.Version), token))
        {
            Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode); var value = await repeated.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token); Assert.AreEqual(paused.Version, value!.Version);
        }
        using (var stale = await a.PostAsJsonAsync(path, new SetWebhookSubscriptionStatusRequest(true, subscription.Version), token))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode); using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync(token)); Assert.AreEqual(WebhookErrorCodes.SubscriptionVersionConflict, problem.RootElement.GetProperty("code").GetString());
        }
        var pausedEvent = Guid.CreateVersion7(); await EnqueueAsync(pausedEvent); Assert.AreEqual(0, await CountAsync(pausedEvent));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ClaimAsync(token)).Count);
        }
        Assert.AreEqual(1, await CountAsync(originalEvent));
        using var resumedResponse = await a.PostAsJsonAsync(path, new SetWebhookSubscriptionStatusRequest(true, paused.Version), token); Assert.AreEqual(HttpStatusCode.OK, resumedResponse.StatusCode);
        var resumed = await resumedResponse.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token); Assert.IsNotNull(resumed); Assert.IsTrue(resumed.IsActive); Assert.AreEqual(paused.Version + 1, resumed.Version);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var claimed = await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ClaimAsync(token); Assert.AreEqual(1, claimed.Count); Assert.AreEqual(subscription.Id, claimed[0].SubscriptionId); Assert.AreEqual(originalEvent, claimed[0].EventId);
        }
        await EnqueueAsync(originalEvent); Assert.AreEqual(1, await CountAsync(originalEvent)); Assert.AreEqual(0, await CountAsync(pausedEvent));
        using var listed = await a.GetAsync("/api/v1/webhooks/subscriptions", token); Assert.AreEqual(HttpStatusCode.OK, listed.StatusCode);
        var listBody = await listed.Content.ReadAsStringAsync(token); Assert.IsFalse(listBody.Contains(secret, StringComparison.Ordinal)); Assert.IsFalse(listBody.Contains("signingSecret", StringComparison.OrdinalIgnoreCase));

        async Task<string> AccessAsync(Guid tenantId, IReadOnlyCollection<string> permissions)
        {
            var access = await factory.CreateHostAccessTokenAsync([.. permissions, "tenancy.tenants.switch"], token);
            return await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, access, tenantId, token);
        }
        async Task EnqueueAsync(Guid eventId)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellation => {
                await scope.ServiceProvider.GetRequiredService<WebhookEventEnqueueService>().EnqueueAsync(tenant.Id, subscription.EventType, eventId,
                    new(subscription.EventType, eventId, tenant.Id, DateTimeOffset.UtcNow, new(Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "console")), cancellation); return 0;
            }, token);
        }
        async Task<int> CountAsync(Guid eventId)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(new("test.webhooks.console.event_count", "SELECT COUNT(*) FROM fn_webhooks_delivery WHERE EventId=@EventId", SqlDataScope.HostOnly), new Dictionary<string, object?> { ["EventId"] = eventId }, token);
        }
    }
}
