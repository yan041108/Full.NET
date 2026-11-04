using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>只替换 HTTP 传输；真实入队、领取、事务、开始/结果记录及查询保持生产实现。</summary>
[TestClass]
public sealed class WebhookDeliveryAttemptTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Attempt_write_failures_preserve_claim_and_completion_atomicity(DatabaseProvider provider, bool completionFailure)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var clock = new AdjustableClock(); var transport = new Receiver();
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs, configureTestServices: services => {
            services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock);
            services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(transport);
        });
        await factory.InitializeAsync(token); using var client = factory.CreateClientForHost("acme.localhost");
        var prepared = await PrepareAsync(factory, client, token); clock.Freeze(); var path = $"/api/v1/webhooks/subscriptions/{prepared.Subscription.Id:D}/deliveries/{prepared.Delivery.Id:D}";
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs); await db.OpenAsync(token);
        // 只拒绝成功落账，失败落账仍可写入，以证明存储异常不会被误判为 HTTP 失败。
        var check = completionFailure ? "OutcomeKey<>'delivered'" : "1=0";
        await db.ExecuteAsync($"ALTER TABLE fn_webhooks_delivery_attempt ADD CONSTRAINT CK_test_webhooks_attempt_write CHECK ({check})");
        await Assert.ThrowsAsync<Exception>(() => ProcessAsync(factory, token));
        var blocked = await client.GetFromJsonAsync<WebhookDeliveryHistoryResponse>(path, token); Assert.IsNotNull(blocked);
        Assert.AreEqual(completionFailure ? "processing" : "pending", blocked.StatusKey); Assert.AreEqual(0, blocked.AttemptCount);
        Assert.AreEqual(completionFailure ? 1 : 0, transport.Count);
        var attempts = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(path + "/attempts", token); Assert.IsNotNull(attempts); Assert.AreEqual(completionFailure ? 1L : 0L, attempts.Total);
        if (completionFailure) { Assert.IsNull(attempts.Items[0].CompletedAtUtc); Assert.IsNull(attempts.Items[0].AppliedToDelivery); }
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_webhooks_delivery_attempt DROP CONSTRAINT CK_test_webhooks_attempt_write" : "ALTER TABLE fn_webhooks_delivery_attempt DROP CHECK CK_test_webhooks_attempt_write");
        if (completionFailure)
        {
            clock.Offset = TimeSpan.FromSeconds(70);
            var unknown = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(path + "/attempts", token); Assert.AreEqual("unconfirmed", unknown!.Items[0].OutcomeKey);
        }
        Assert.AreEqual(1, await ProcessAsync(factory, token));
        var final = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(path + "/attempts", token); Assert.IsNotNull(final);
        Assert.AreEqual(completionFailure ? 2L : 1L, final.Total); Assert.AreEqual(1, final.Items.Count(row => row.OutcomeKey == "delivered" && row.AppliedToDelivery == true));
        Assert.AreEqual(1, transport.UniqueEventCount);
        var delivered = await client.GetFromJsonAsync<WebhookDeliveryHistoryResponse>(path, token); Assert.AreEqual("delivered", delivered!.StatusKey); Assert.AreEqual(1, delivered.AttemptCount);
        Assert.AreEqual(0, await ProcessAsync(factory, token));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Late_acknowledgement_is_retained_without_overwriting_the_new_lease(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var clock = new AdjustableClock(); var transport = new Receiver { PauseFirst = true };
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs, configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(transport); });
        await factory.InitializeAsync(token); using var client = factory.CreateClientForHost("acme.localhost"); var prepared = await PrepareAsync(factory, client, token); clock.Freeze();
        var path = $"/api/v1/webhooks/subscriptions/{prepared.Subscription.Id:D}/deliveries/{prepared.Delivery.Id:D}";
        var old = ProcessAsync(factory, token);
        try
        {
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token); clock.Offset = TimeSpan.FromSeconds(70);
            Assert.AreEqual(1, await ProcessAsync(factory, token));
        }
        finally { transport.Release.TrySetResult(); await old; }
        var summary = await client.GetFromJsonAsync<WebhookDeliveryHistoryResponse>(path, token); Assert.AreEqual("delivered", summary!.StatusKey); Assert.AreEqual(1, summary.AttemptCount);
        var result = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(path + "/attempts?pageSize=1", token); Assert.AreEqual(2L, result!.Total); Assert.AreEqual(1, result.Items.Count);
        var page2 = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(path + "/attempts?page=2&pageSize=1", token);
        var rows = result.Items.Concat(page2!.Items).ToArray(); Assert.IsTrue(rows.All(row => row.OutcomeKey == "delivered" && row.HttpStatus == 200 && row.CompletedAtUtc is not null));
        Assert.AreEqual(1, rows.Count(row => row.AppliedToDelivery == false)); Assert.AreEqual(1, rows.Count(row => row.AppliedToDelivery == true));
        Assert.AreEqual(2, transport.Count); Assert.AreEqual(1, transport.UniqueEventCount);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs); await db.OpenAsync(token);
        var first = rows.Single(row => row.AppliedToDelivery == false);
        Assert.AreEqual(0, await db.ExecuteAsync("UPDATE fn_webhooks_delivery_attempt SET OutcomeKey='failed',HttpStatus=503 WHERE Id=@Id AND OutcomeKey='started' AND CompletedAtUtc IS NULL", new { first.Id }));
        var unchanged = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(path + "/attempts", token); Assert.IsTrue(unchanged!.Items.All(row => row.HttpStatus == 200));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Completed_http_failure_records_safe_status_and_backs_off_from_response_time(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var clock = new AdjustableClock(); var transport = new Receiver { Status = HttpStatusCode.ServiceUnavailable, BeforeResponse = () => clock.Offset = TimeSpan.FromSeconds(10) };
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs, configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(transport); });
        await factory.InitializeAsync(token); using var client = factory.CreateClientForHost("acme.localhost"); var prepared = await PrepareAsync(factory, client, token); clock.Freeze();
        Assert.AreEqual(1, await ProcessAsync(factory, token)); Assert.AreEqual(1, transport.Count);
        var path = $"/api/v1/webhooks/subscriptions/{prepared.Subscription.Id:D}/deliveries/{prepared.Delivery.Id:D}";
        var summary = await client.GetFromJsonAsync<WebhookDeliveryHistoryResponse>(path, token); Assert.AreEqual("pending", summary!.StatusKey); Assert.AreEqual(clock.UtcNow.AddSeconds(2), summary.NextAttemptAtUtc);
        var attempts = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(path + "/attempts", token); Assert.AreEqual(1L, attempts!.Total);
        var row = attempts.Items.Single(); Assert.AreEqual("failed", row.OutcomeKey); Assert.AreEqual(503, row.HttpStatus); Assert.AreEqual("webhooks.delivery.http_error", row.ErrorCode); Assert.AreEqual(true, row.AppliedToDelivery);
    }

    private static async Task<(WebhookSubscriptionResponse Subscription, WebhookDeliveryHistoryResponse Delivery)> PrepareAsync(FullNetApiFactory factory, HttpClient client, CancellationToken token)
    {
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, token); using var host = factory.CreateClientForHost("localhost");
        var access = await factory.CreateHostAccessTokenAsync([WebhookPermissions.SubscriptionsRead, WebhookPermissions.SubscriptionsManage, WebhookPermissions.DeliveriesRead, "tenancy.tenants.switch"], token);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, access, tenant.Id, token));
        using var created = await client.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new CreateWebhookSubscriptionRequest("fullnet.workflow.instance.completed", "https://93.184.216.34/hooks", "attempt-independent-secret"), token); Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
        var subscription = (await created.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token))!; var eventId = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        { scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost(); await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellation => { await scope.ServiceProvider.GetRequiredService<WebhookEventEnqueueService>().EnqueueAsync(tenant.Id, subscription.EventType, eventId, new(subscription.EventType, eventId, tenant.Id, DateTimeOffset.UtcNow, new(Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "attempt")), cancellation); return 0; }, token); }
        var page = await client.GetFromJsonAsync<PagedResult<WebhookDeliveryHistoryResponse>>($"/api/v1/webhooks/subscriptions/{subscription.Id:D}/deliveries", token); Assert.AreEqual(1, page!.Items.Count); return (subscription, page.Items[0]);
    }
    private static async Task<int> ProcessAsync(FullNetApiFactory factory, CancellationToken token)
    { await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost(); return await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ProcessPendingAsync(token); }
    private sealed class AdjustableClock : IClock { private DateTimeOffset? frozen; internal TimeSpan Offset; internal void Freeze() { var now = DateTimeOffset.UtcNow; frozen = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero); } public DateTimeOffset UtcNow => (frozen ?? DateTimeOffset.UtcNow) + Offset; }
    private sealed class Receiver : IHttpClientFactory
    {
        private readonly HashSet<Guid> events = []; private int count;
        internal int Count => Volatile.Read(ref count); internal int UniqueEventCount { get { lock(events) return events.Count; } }
        internal bool PauseFirst;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal Action? BeforeResponse;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HttpClient CreateClient(string name) => new(new Handler(async (request, token) => {
            var current = Interlocked.Increment(ref count); var eventId = Guid.Parse(request.Headers.GetValues("X-FullNet-Event-Id").Single()); lock(events) events.Add(eventId);
            if (PauseFirst && current == 1) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            BeforeResponse?.Invoke(); return new HttpResponseMessage(Status);
        }));
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
}
