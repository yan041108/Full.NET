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
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实双库原失败、重新排队、审计与 Worker；仅替换 HTTP 传输，不替换授权判断。</summary>
[TestClass]
public sealed class WebhookDeliveryReplayTests
{
    private static readonly string[] Rights = [WebhookPermissions.SubscriptionsRead, WebhookPermissions.SubscriptionsManage,
        WebhookPermissions.SubscriptionsSetStatus, WebhookPermissions.DeliveriesRead, WebhookPermissions.DeliveriesReplay];

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Replay_preserves_original_event_and_attempts_and_returns_the_same_committed_receipt(DatabaseProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = timeout.Token;
        await using var f = await Fixture.CreateAsync(provider, token);
        var failed = await f.DetailAsync(token); Assert.AreEqual("failed", failed.StatusKey); Assert.AreEqual(1, failed.AttemptCount);
        var request = new ReplayWebhookDeliveryRequest(Guid.CreateVersion7(), failed.Version);
        await f.StatusAsync(false, 1, token);
        using (var paused = await f.Client.PostAsJsonAsync(f.Path + "/replays", request, token)) Assert.AreEqual(HttpStatusCode.Conflict, paused.StatusCode);
        await f.StatusAsync(true, 2, token);
        var original = await f.Database.QuerySingleAsync<Payload>("SELECT EventId,PayloadBody,PayloadDigest FROM fn_webhooks_delivery WHERE Id=@Id", new { Id = failed.Id });
        var receipt = await f.ReplayAsync(request, token); Assert.AreEqual(failed.Version + 1, receipt.Version); Assert.AreEqual(failed.EventId, receipt.EventId); Assert.AreEqual(1, receipt.AttemptCount);
        Assert.AreEqual(receipt, await f.ReplayAsync(request, token));
        Assert.AreEqual(receipt, await f.Client.GetFromJsonAsync<WebhookDeliveryReplayReceipt>(f.Path + $"/replays/{request.OperationId:D}", token));
        Assert.AreEqual("pending", (await f.DetailAsync(token)).StatusKey);
        using (var stale = await f.Client.PostAsJsonAsync(f.Path + "/replays", request with { OperationId = Guid.CreateVersion7() }, token)) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        using (var mismatch = await f.Client.PostAsJsonAsync(f.Path + "/replays", request with { Version = receipt.Version }, token)) Assert.AreEqual(HttpStatusCode.Conflict, mismatch.StatusCode);
        f.Receiver.Status = HttpStatusCode.OK; Assert.AreEqual(1, await f.ProcessAsync(token));
        var delivered = await f.DetailAsync(token); Assert.AreEqual("delivered", delivered.StatusKey); Assert.AreEqual(2, delivered.AttemptCount);
        Assert.AreEqual(receipt, await f.ReplayAsync(request, token)); Assert.AreEqual(0, await f.ProcessAsync(token));
        using (var forbiddenState = await f.Client.PostAsJsonAsync(f.Path + "/replays", new ReplayWebhookDeliveryRequest(Guid.CreateVersion7(), delivered.Version), token)) Assert.AreEqual(HttpStatusCode.Conflict, forbiddenState.StatusCode);
        Assert.AreEqual(original, await f.Database.QuerySingleAsync<Payload>("SELECT EventId,PayloadBody,PayloadDigest FROM fn_webhooks_delivery WHERE Id=@Id", new { Id = failed.Id }));
        var attempts = await f.Client.GetFromJsonAsync<PagedResult<WebhookDeliveryAttemptResponse>>(f.Path + "/attempts", token); Assert.AreEqual(2L, attempts!.Total);
        Assert.AreEqual(2, f.Receiver.Bodies.Count); Assert.IsTrue(f.Receiver.Bodies.All(body => body == original.PayloadBody));
        Assert.AreEqual(1L, await f.Database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery_replay WHERE DeliveryId=@Id AND ActorUserId IS NOT NULL AND ActorSessionId IS NOT NULL", new { Id = failed.Id }));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Competing_replays_queue_once_and_cannot_cross_tenant_or_permission_boundaries(DatabaseProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = timeout.Token;
        await using var f = await Fixture.CreateAsync(provider, token); var failed = await f.DetailAsync(token);
        var first = new ReplayWebhookDeliveryRequest(Guid.CreateVersion7(), failed.Version); var second = first with { OperationId = Guid.CreateVersion7() };
        var responses = await Task.WhenAll(f.Client.PostAsJsonAsync(f.Path + "/replays", first, token), f.Client.PostAsJsonAsync(f.Path + "/replays", second, token));
        try { Assert.AreEqual(1, responses.Count(row => row.StatusCode == HttpStatusCode.OK)); Assert.AreEqual(1, responses.Count(row => row.StatusCode == HttpStatusCode.Conflict)); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.AreEqual(1L, await f.Database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery_replay WHERE DeliveryId=@Id", new { Id = failed.Id }));
        Assert.AreEqual(failed.Version + 1, (await f.DetailAsync(token)).Version);
        var committedOperation = await f.Database.ExecuteScalarAsync<Guid>("SELECT OperationId FROM fn_webhooks_delivery_replay WHERE DeliveryId=@Id", new { Id = failed.Id });
        using (var anotherActor = f.Factory.CreateClientForHost("acme.localhost"))
        {
            anotherActor.DefaultRequestHeaders.Authorization = new("Bearer", await f.AccessAsync(failed.TenantId, Rights, token));
            using var hidden = await anotherActor.GetAsync(f.Path + $"/replays/{committedOperation:D}", token); Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
            using var collision = await anotherActor.PostAsJsonAsync(f.Path + "/replays", first with { OperationId = committedOperation }, token); Assert.AreEqual(HttpStatusCode.Conflict, collision.StatusCode);
        }
        using (var other = await f.Client.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new CreateWebhookSubscriptionRequest("fullnet.other.completed", "https://93.184.216.34/other", "replay-independent-secret"), token))
        {
            var subscription = (await other.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token))!;
            var path = $"/api/v1/webhooks/subscriptions/{subscription.Id:D}/deliveries/{failed.Id:D}";
            using var hidden = await f.Client.GetAsync(path + $"/replays/{committedOperation:D}", token); Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
            using var wrong = await f.Client.PostAsJsonAsync(path + "/replays", first with { OperationId = committedOperation }, token); Assert.AreEqual(HttpStatusCode.NotFound, wrong.StatusCode);
        }
        foreach (var excluded in new[] { WebhookPermissions.SubscriptionsRead, WebhookPermissions.DeliveriesRead, WebhookPermissions.DeliveriesReplay })
        {
            using var denied = f.Factory.CreateClientForHost("acme.localhost"); denied.DefaultRequestHeaders.Authorization = new("Bearer", await f.AccessAsync(failed.TenantId, Rights.Where(right => right != excluded).ToArray(), token));
            using var response = await denied.PostAsJsonAsync(f.Path + "/replays", first, token); Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        foreach (var version in new[] { 0, int.MaxValue - 2, int.MaxValue })
        { using var invalid = await f.Client.PostAsJsonAsync(f.Path + "/replays", first with { Version = version }, token); Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using (var invalid = await f.Client.PostAsJsonAsync(f.Path + "/replays", first with { OperationId = Guid.Empty }, token)) Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        var domain = "replay-b-" + Guid.CreateVersion7().ToString("N") + ".localhost"; TenantSummary b;
        await using (var scope = f.Factory.Services.CreateAsyncScope())
        { scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost(); var result = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(new("replay-b-" + Guid.CreateVersion7().ToString("N"), "Replay B", domain), token); Assert.IsTrue(result.IsSuccess); b = result.Value!; }
        using var foreign = f.Factory.CreateClientForHost(domain); foreign.DefaultRequestHeaders.Authorization = new("Bearer", await f.AccessAsync(b.Id, Rights, token));
        using (var hidden = await foreign.PostAsJsonAsync(f.Path + "/replays", first, token)) Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var hidden = await foreign.GetAsync(f.Path + $"/replays/{first.OperationId:D}", token)) Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(1, f.Receiver.Bodies.Count);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Audit_insert_failure_rolls_back_queue_and_the_original_operation_can_be_retried(DatabaseProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = timeout.Token;
        await using var f = await Fixture.CreateAsync(provider, token); var before = await f.DetailAsync(token); var request = new ReplayWebhookDeliveryRequest(Guid.CreateVersion7(), before.Version);
        await f.Database.ExecuteAsync("ALTER TABLE fn_webhooks_delivery_replay ADD CONSTRAINT CK_test_webhooks_replay_insert CHECK (1=0)");
        using (var failed = await f.Client.PostAsJsonAsync(f.Path + "/replays", request, token)) Assert.AreEqual(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.AreEqual(before, await f.DetailAsync(token)); Assert.AreEqual(0L, await f.Database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery_replay"));
        await f.Database.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_webhooks_delivery_replay DROP CONSTRAINT CK_test_webhooks_replay_insert" : "ALTER TABLE fn_webhooks_delivery_replay DROP CHECK CK_test_webhooks_replay_insert");
        Assert.AreEqual(before.Version + 1, (await f.ReplayAsync(request, token)).Version);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Actual_session_expiry_before_commit_rolls_back_both_queue_and_receipt(DatabaseProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = timeout.Token;
        await using var f = await Fixture.CreateAsync(provider, token, expireAtCommit: true); var before = await f.DetailAsync(token); f.ExpireBeforeRead = true;
        using (var denied = await f.Client.PostAsJsonAsync(f.Path + "/replays", new ReplayWebhookDeliveryRequest(Guid.CreateVersion7(), before.Version), token)) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        f.Clock.Offset = TimeSpan.Zero;
        Assert.AreEqual(before, await f.DetailAsync(token)); Assert.AreEqual(0L, await f.Database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery_replay")); Assert.AreEqual(1, f.Receiver.Bodies.Count);
    }

    private sealed record Payload(Guid EventId, string PayloadBody, string PayloadDigest);
    private sealed class Clock : IClock { internal TimeSpan Offset; public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Offset; }
    private sealed class ExpiringAuthorization(IIndependentCurrentSessionAuthorization actual, Action before) : IIndependentCurrentSessionAuthorization
    { public bool HasIndependentReadCapacity => actual.HasIndependentReadCapacity; public Task<AuthorizedSessionActor?> AuthorizeAsync(string permission, CancellationToken token) { before(); return actual.AuthorizeAsync(permission, token); } }
    private sealed class Receiver : IHttpClientFactory
    {
        internal HttpStatusCode Status = HttpStatusCode.ServiceUnavailable; internal readonly List<string> Bodies = [];
        public HttpClient CreateClient(string name) => new(new Handler(async (request, token) => { var body = await request.Content!.ReadAsStringAsync(token); Assert.AreEqual(WebhookSignatureHelper.ComputeSignature("replay-independent-secret", body), request.Headers.GetValues("X-FullNet-Signature").Single()); Bodies.Add(body); return new HttpResponseMessage(Status); }));
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }

    private sealed class Fixture : IAsyncDisposable
    {
        internal FullNetApiFactory Factory = null!; internal HttpClient Client = null!; internal DbConnection Database = null!; internal string Path = "";
        internal readonly Clock Clock = new(); internal readonly Receiver Receiver = new(); internal bool ExpireBeforeRead;
        internal static async Task<Fixture> CreateAsync(DatabaseProvider provider, CancellationToken token, bool expireAtCommit = false)
        {
            var f = new Fixture();
            try
            {
                var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
                f.Factory = new(provider, cs, configureTestServices: services => {
                    services.RemoveAll<IClock>(); services.AddSingleton<IClock>(f.Clock); services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(f.Receiver);
                    services.Configure<WebhookDeliveryWorkerOptions>(options => options.MaxAttempts = 1);
                    if (expireAtCommit) { services.AddScoped<IndependentCurrentSessionAuthorization>(); services.RemoveAll<IIndependentCurrentSessionAuthorization>(); services.AddScoped<IIndependentCurrentSessionAuthorization>(sp => new ExpiringAuthorization(sp.GetRequiredService<IndependentCurrentSessionAuthorization>(), () => { if (f.ExpireBeforeRead) { f.ExpireBeforeRead = false; f.Clock.Offset = TimeSpan.FromDays(2); } })); }
                });
                await f.Factory.InitializeAsync(token); f.Client = f.Factory.CreateClientForHost("acme.localhost"); var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(f.Client, token);
                f.Client.DefaultRequestHeaders.Authorization = new("Bearer", await f.AccessAsync(tenant.Id, Rights, token));
                using var created = await f.Client.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new CreateWebhookSubscriptionRequest("fullnet.workflow.instance.completed", "https://93.184.216.34/replay", "replay-independent-secret"), token); Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
                var subscription = (await created.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token))!; var eventId = Guid.CreateVersion7();
                await using (var scope = f.Factory.Services.CreateAsyncScope())
                { scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost(); await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellation => { await scope.ServiceProvider.GetRequiredService<WebhookEventEnqueueService>().EnqueueAsync(tenant.Id, subscription.EventType, eventId, new(subscription.EventType, eventId, tenant.Id, DateTimeOffset.UtcNow, new(Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "original")), cancellation); return 0; }, token); }
                Assert.AreEqual(1, await f.ProcessAsync(token));
                var rows = await f.Client.GetFromJsonAsync<PagedResult<WebhookDeliveryHistoryResponse>>($"/api/v1/webhooks/subscriptions/{subscription.Id:D}/deliveries", token); var delivery = rows!.Items.Single();
                f.Path = $"/api/v1/webhooks/subscriptions/{subscription.Id:D}/deliveries/{delivery.Id:D}";
                f.Database = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs); await f.Database.OpenAsync(token); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        internal async Task<string> AccessAsync(Guid tenant, IReadOnlyCollection<string> rights, CancellationToken token) { using var host = Factory.CreateClientForHost("localhost"); return await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, await Factory.CreateHostAccessTokenAsync([.. rights, "tenancy.tenants.switch"], token), tenant, token); }
        internal async Task<int> ProcessAsync(CancellationToken token) { await using var scope = Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost(); return await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ProcessPendingAsync(token); }
        internal async Task<WebhookDeliveryHistoryResponse> DetailAsync(CancellationToken token) => (await Client.GetFromJsonAsync<WebhookDeliveryHistoryResponse>(Path, token))!;
        internal async Task<WebhookDeliveryReplayReceipt> ReplayAsync(ReplayWebhookDeliveryRequest request, CancellationToken token) { using var response = await Client.PostAsJsonAsync(Path + "/replays", request, token); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token)); return (await response.Content.ReadFromJsonAsync<WebhookDeliveryReplayReceipt>(token))!; }
        internal async Task StatusAsync(bool active, int version, CancellationToken token) { using var response = await Client.PostAsJsonAsync(Path[..Path.IndexOf("/deliveries", StringComparison.Ordinal)] + "/status", new SetWebhookSubscriptionStatusRequest(active, version), token); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); }
        public async ValueTask DisposeAsync() { Client?.Dispose(); if (Database is not null) await Database.DisposeAsync(); Factory?.Dispose(); }
    }
}
