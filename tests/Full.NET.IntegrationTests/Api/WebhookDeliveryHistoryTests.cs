using System.Net;
using System.Net.Http.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实入队及生产状态 SQL 形成历史；只核对安全快照，不发送外部 HTTP。</summary>
[TestClass]
public sealed class WebhookDeliveryHistoryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task History_is_paged_owner_bound_and_redacts_payload_secret_and_raw_errors(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(token);
        using var host = factory.CreateClientForHost("localhost"); using var a = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(a, token);
        var permissions = new[] { WebhookPermissions.SubscriptionsRead, WebhookPermissions.SubscriptionsManage, WebhookPermissions.DeliveriesRead };
        a.DefaultRequestHeaders.Authorization = new("Bearer", await AccessAsync(tenant.Id, permissions));
        var subscription = await CreateAsync("fullnet.workflow.instance.completed"); var other = await CreateAsync("fullnet.other.completed");
        var path = $"/api/v1/webhooks/subscriptions/{subscription.Id:D}/deliveries";
        var events = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        foreach (var eventId in events)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellation => {
                await scope.ServiceProvider.GetRequiredService<WebhookEventEnqueueService>().EnqueueAsync(tenant.Id, subscription.EventType, eventId,
                    new(subscription.EventType, eventId, tenant.Id, DateTimeOffset.UtcNow, new(Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "private-payload-marker")), cancellation); return 0;
            }, token);
        }
        // 故障夹具写入当前真实领取的记录；原始异常中故意带私有地址，验证读取边界不会转发。
        foreach (var failure in new[] { "HTTP 503", "private-error-marker https://internal.example/?token=private-token", "Webhook delivery timed out.", "delivered" })
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var claimed = await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ClaimAsync(token); Assert.AreEqual(1, claimed.Count); var row = claimed[0];
            var parameters = new Dictionary<string, object?> { ["DeliveryId"] = row.Id, ["Version"] = row.Version, ["UpdatedAtUtc"] = DateTimeOffset.UtcNow,
                ["Status"] = failure == "delivered" ? WebhookDeliveryStatuses.Delivered : failure == "HTTP 503" ? WebhookDeliveryStatuses.Pending : WebhookDeliveryStatuses.Failed,
                ["LastError"] = failure, ["NextAttemptAtUtc"] = failure == "HTTP 503" ? DateTimeOffset.UtcNow.AddHours(1) : null };
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(failure == "delivered" ? WebhookDeliverySql.MarkDelivered : WebhookDeliverySql.MarkFailed, parameters, token));
        }
        var all = new List<WebhookDeliveryHistoryResponse>();
        for (var page = 1; page <= 3; page++)
        {
            using var response = await a.GetAsync(path + $"?page={page}&pageSize=2", token); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync(token);
            foreach (var forbidden in new[] { "private-payload-marker", "private-error-marker", "private-token", "signingSecret", "payloadBody", "lastError\"" }) Assert.IsFalse(raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
            var result = await response.Content.ReadFromJsonAsync<PagedResult<WebhookDeliveryHistoryResponse>>(token); Assert.IsNotNull(result); Assert.AreEqual(5L, result.Total); Assert.AreEqual(page, result.Page); Assert.AreEqual(2, result.PageSize); all.AddRange(result.Items);
        }
        Assert.AreEqual(5, all.Count); Assert.AreEqual(5, all.Select(row => row.Id).Distinct().Count());
        Assert.IsTrue(all.All(row => row.TenantId == tenant.Id && row.SubscriptionId == subscription.Id));
        Assert.AreEqual(2, all.Count(row => row.StatusKey == "pending")); Assert.AreEqual(2, all.Count(row => row.StatusKey == "failed")); Assert.AreEqual(1, all.Count(row => row.StatusKey == "delivered"));
        var http = all.Single(row => row.LastHttpStatus == 503); Assert.AreEqual("webhooks.delivery.http_error", http.LastErrorCode); Assert.AreEqual(1, http.AttemptCount);
        Assert.IsTrue(all.Any(row => row.LastErrorCode == "webhooks.delivery.timeout")); Assert.IsTrue(all.Any(row => row.LastErrorCode == "webhooks.delivery.failed"));
        using (var filtered = await a.GetAsync(path + "?statusKey=failed&page=1&pageSize=20", token))
        { var result = await filtered.Content.ReadFromJsonAsync<PagedResult<WebhookDeliveryHistoryResponse>>(token); Assert.AreEqual(2L, result!.Total); Assert.IsTrue(result.Items.All(row => row.StatusKey == "failed")); }
        using (var detail = await a.GetAsync(path + $"/{http.Id:D}", token))
        { Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode); Assert.AreEqual(http, await detail.Content.ReadFromJsonAsync<WebhookDeliveryHistoryResponse>(token)); }
        using (var wrong = await a.GetAsync($"/api/v1/webhooks/subscriptions/{other.Id:D}/deliveries/{http.Id:D}", token)) Assert.AreEqual(HttpStatusCode.NotFound, wrong.StatusCode);
        var attemptsPath = path + $"/{http.Id:D}/attempts";
        using (var attempts = await a.GetAsync(attemptsPath, token))
        {
            Assert.AreEqual(HttpStatusCode.OK, attempts.StatusCode);
            var page = await attempts.Content.ReadFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(token);
            Assert.AreEqual(1L, page!.Total); Assert.AreEqual("unconfirmed", page.Items.Single().OutcomeKey);
            var raw = await attempts.Content.ReadAsStringAsync(token);
            foreach (var forbidden in new[] { "private-payload-marker", "private-error-marker", "signingSecret", "payloadBody", "claimVersion" }) Assert.IsFalse(raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
        using (var wrong = await a.GetAsync($"/api/v1/webhooks/subscriptions/{other.Id:D}/deliveries/{http.Id:D}/attempts", token)) Assert.AreEqual(HttpStatusCode.NotFound, wrong.StatusCode);
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=2147483647&pageSize=100" })
        { using var invalid = await a.GetAsync(attemptsPath + query, token); Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode); }
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=2147483647&pageSize=100", "?statusKey=unknown" })
        { using var invalid = await a.GetAsync(path + query, token); Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode); }
        foreach (var rights in new[] { new[] { WebhookPermissions.SubscriptionsRead }, new[] { WebhookPermissions.DeliveriesRead } })
        { using var client = factory.CreateClientForHost("acme.localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", await AccessAsync(tenant.Id, rights)); using var denied = await client.GetAsync(path, token); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); using var attemptDenied = await client.GetAsync(attemptsPath, token); Assert.AreEqual(HttpStatusCode.Forbidden, attemptDenied.StatusCode); }
        var domain = "history-b-" + Guid.CreateVersion7().ToString("N") + ".localhost"; TenantSummary b;
        await using (var scope = factory.Services.CreateAsyncScope())
        { scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost(); var result = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(new("history-b-" + Guid.CreateVersion7().ToString("N"), "History B", domain), token); Assert.IsTrue(result.IsSuccess); b = result.Value!; }
        using var foreign = factory.CreateClientForHost(domain); foreign.DefaultRequestHeaders.Authorization = new("Bearer", await AccessAsync(b.Id, permissions));
        using (var hidden = await foreign.GetAsync(path, token)) Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var hidden = await foreign.GetAsync(path + $"/{http.Id:D}", token)) Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var hidden = await foreign.GetAsync(attemptsPath, token)) Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        async Task<string> AccessAsync(Guid tenantId, IReadOnlyCollection<string> rights) => await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, await factory.CreateHostAccessTokenAsync([.. rights, "tenancy.tenants.switch"], token), tenantId, token);
        async Task<WebhookSubscriptionResponse> CreateAsync(string eventType)
        { using var response = await a.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new CreateWebhookSubscriptionRequest(eventType, "https://example.com/history", "history-independent-secret"), token); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token))!; }
    }
}
