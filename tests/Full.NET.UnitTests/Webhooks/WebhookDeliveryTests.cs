using Full.NET.Data.Dapper;
using System.Net;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Webhooks.Serialization;
using Full.NET.Modules.Webhooks;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;
using Full.NET.Modules.Webhooks.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Webhooks;

/// <summary>单元测试（Webhook）：Webhook Delivery。</summary>
[TestClass]
public sealed class WebhookDeliveryTests
{
    [TestMethod]
    public void Worker_registration_includes_delivery_processor_and_dependencies()
    {
        var services = new ServiceCollection();
        new WebhooksModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(WebhookDeliveryBatchProcessor)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(WebhookSigningSecretProtector)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHttpClientFactory)));
    }

    [TestMethod]
    public async Task Worker_iteration_uses_host_context_for_delivery_sql()
    {
        var tenant = new CurrentTenantAccessor();
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WebhookDeliveryWorkItem>(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsTrue(tenant.IsHost);
                return Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([]);
            });
        var processor = new WebhookDeliveryBatchProcessor(
            query,
            Substitute.For<ICommandExecutor>(),
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),
            Substitute.For<IHttpClientFactory>(),
            new WebhookSigningSecretProtector(DataProtectionProvider.Create(nameof(WebhookDeliveryTests))),
            new FixedClock(DateTimeOffset.Parse("2026-09-17T00:00:00Z")),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new WebhookDeliveryWorkerOptions()),
            NullLogger<WebhookDeliveryBatchProcessor>.Instance);
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantContextWriter>(_ => tenant);
        services.AddScoped(_ => processor);
        using var provider = services.BuildServiceProvider();
        var worker = new WebhookDeliveryHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WebhookDeliveryWorkerOptions()),
            NullLogger<WebhookDeliveryHostedProcessor>.Instance);

        Assert.AreEqual(0, await worker.ProcessOnceAsync(CancellationToken.None));
        Assert.IsFalse(tenant.IsHost);
        await query.Received(1).QueryAsync<WebhookDeliveryWorkItem>(
            WebhookDeliverySql.ClaimPendingSqlServer,
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void ValidateTargetUrl_rejects_relative_urls()
    {
        var result = WebhookSubscriptionManagementService.ValidateTargetUrl("/hooks/callback");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ValidateTargetUrl_rejects_http_urls()
    {
        var result = WebhookSubscriptionManagementService.ValidateTargetUrl("http://example.com/hooks/callback");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ValidateTargetUrl_rejects_loopback_literal_hosts()
    {
        var result = WebhookSubscriptionManagementService.ValidateTargetUrl("https://127.0.0.1/hooks/callback");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ValidateTargetUrl_rejects_private_literal_hosts()
    {
        var result = WebhookSubscriptionManagementService.ValidateTargetUrl("https://10.0.0.8/hooks/callback");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ValidateTargetUrl_allows_public_https_hosts()
    {
        var result = WebhookSubscriptionManagementService.ValidateTargetUrl("https://example.com/hooks/callback");
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task DeliverySsrfGuard_rejects_loopback_literal_hosts()
    {
        var (allowed, reason) = await WebhookDeliverySsrfGuard.ValidateAsync(
            new Uri("https://127.0.0.1/hooks/callback"),
            CancellationToken.None);
        Assert.IsFalse(allowed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(reason));
    }

    [TestMethod]
    public void ComputeSignature_matches_hmac_sha256_hex_uppercase()
    {
        const string payload = "{\"eventId\":\"00000000-0000-7000-8000-000000000001\"}";
        var signature = WebhookSignatureHelper.ComputeSignature("demo-secret", payload);
        Assert.AreEqual(64, signature.Length);
        Assert.IsTrue(signature.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F'));
        var envelope = new WebhookEventEnvelope("completed", Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, new WebhookWorkflowInstanceCompletedData(
                Guid.NewGuid(), Guid.NewGuid(), "申请", "request-1"));
        // 源生成必须保留原签名原文；字段大小写或转义变化都会改变 HMAC。
        var previousPayload = JsonSerializer.Serialize(envelope);
        var generatedPayload = JsonSerializer.Serialize(envelope,
            WebhookPayloadJsonSerializerContext.Default.WebhookEventEnvelope);
        Assert.AreEqual(previousPayload, generatedPayload);
        Assert.AreEqual(WebhookSignatureHelper.ComputeSignature("demo-secret", previousPayload),
            WebhookSignatureHelper.ComputeSignature("demo-secret", generatedPayload));
    }

    [TestMethod]
    public void SigningSecretProtector_roundtrips_secret()
    {
        var provider = DataProtectionProvider.Create(nameof(WebhookDeliveryTests));
        var protector = new WebhookSigningSecretProtector(provider);
        var protectedValue = protector.Protect("demo-secret");
        Assert.AreEqual("demo-secret", protector.Unprotect(protectedValue));
    }

    [TestMethod]
    public async Task ProcessPendingAsync_posts_signed_payload_and_marks_delivered()
    {
        const string payload = "{\"eventType\":\"demo\"}";
        const string secret = "demo-secret";
        var deliveryId = Guid.Parse("00000000-0000-7000-8000-000000000011");
        var subscriptionId = Guid.Parse("00000000-0000-7000-8000-000000000012");
        var eventId = Guid.Parse("00000000-0000-7000-8000-000000000013");
        var tenantId = Guid.Parse("00000000-0000-7000-8000-000000000014");
        var capturedSignature = string.Empty;
        var handler = new CaptureHandler((request, cancellationToken) =>
        {
            capturedSignature = request.Headers.GetValues("X-FullNet-Signature").Single();
            Assert.AreEqual(eventId.ToString("D"), request.Headers.GetValues("X-FullNet-Event-Id").Single());
            Assert.AreEqual(deliveryId.ToString("D"), request.Headers.GetValues("X-FullNet-Delivery-Id").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(WebhookHttpClientNames.Delivery).Returns(httpClient);

        var protector = new WebhookSigningSecretProtector(
            DataProtectionProvider.Create(nameof(WebhookDeliveryTests)));
        var protectedSecret = protector.Protect(secret);

        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WebhookDeliveryWorkItem>(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([
                new WebhookDeliveryWorkItem(
                    deliveryId,
                    subscriptionId,
                    eventId,
                    payload,
                    0,
                    1,
                    tenantId,
                    // 使用公网字面量 IP，避免 CI 上 example.com DNS 偶发失败导致 SSRF 校验未发起 HTTP。
                    "https://93.184.216.34/hooks/callback",
                    protectedSecret,
                    "fullnet.workflow.instance.completed"),
            ]), Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([]));

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator());
        var processor = new WebhookDeliveryBatchProcessor(
            query,
            command,
            transaction,
            httpClientFactory,
            protector,
            new FixedClock(DateTimeOffset.Parse("2026-09-17T00:00:00Z")),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new WebhookDeliveryWorkerOptions()),
            NullLogger<WebhookDeliveryBatchProcessor>.Instance);

        var processed = await processor.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(1, processed);
        Assert.AreEqual(
            WebhookSignatureHelper.ComputeSignature(secret, payload),
            capturedSignature);
        await command.Received(1).ExecuteAsync(
            WebhookDeliverySql.MarkDelivered,
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ProcessPendingAsync_marks_failed_when_target_url_is_blocked()
    {
        var deliveryId = Guid.Parse("00000000-0000-7000-8000-000000000021");
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WebhookDeliveryWorkItem>(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([
                new WebhookDeliveryWorkItem(
                    deliveryId,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    "{}",
                    0,
                    1,
                    Guid.CreateVersion7(),
                    "https://127.0.0.1/hooks/callback",
                    "protected",
                    "fullnet.workflow.instance.completed"),
            ]), Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([]));

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var processor = new WebhookDeliveryBatchProcessor(
            query,
            command,
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),
            Substitute.For<IHttpClientFactory>(),
            new WebhookSigningSecretProtector(
                DataProtectionProvider.Create(nameof(WebhookDeliveryTests))),
            new FixedClock(DateTimeOffset.Parse("2026-09-17T00:00:00Z")),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new WebhookDeliveryWorkerOptions()),
            NullLogger<WebhookDeliveryBatchProcessor>.Instance);

        var processed = await processor.ProcessPendingAsync(CancellationToken.None);
        Assert.AreEqual(1, processed);
        await command.Received(1).ExecuteAsync(
            WebhookDeliverySql.MarkFailed,
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(
            WebhookDeliverySql.MarkDelivered,
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Timeout_is_counted_and_does_not_abort_the_following_delivery()
    {
        var protector = new WebhookSigningSecretProtector(DataProtectionProvider.Create(nameof(WebhookDeliveryTests)));
        var query = Substitute.For<IQueryExecutor>();
        var first = new WebhookDeliveryWorkItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}", 0, 1,
            Guid.NewGuid(), "https://93.184.216.34/hook", protector.Protect("secret"), "event");
        var second = first with { Id = Guid.NewGuid() };
        query.QueryAsync<WebhookDeliveryWorkItem>(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([first]),
                Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([second]),
                Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([]));
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>()).Returns(1);
        var calls = 0;
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(WebhookHttpClientNames.Delivery).Returns(_ => new HttpClient(new CaptureHandler((_, _) => ++calls == 1
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("provider timeout"))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        var processor = new WebhookDeliveryBatchProcessor(query, commands,
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), clients, protector,
            new FixedClock(DateTimeOffset.UtcNow), Options.Create(new DatabaseOptions()),
            Options.Create(new WebhookDeliveryWorkerOptions()), NullLogger<WebhookDeliveryBatchProcessor>.Instance);
        Assert.AreEqual(2, await processor.ProcessPendingAsync(default));
        await commands.Received(1).ExecuteAsync(WebhookDeliverySql.MarkFailed,
            Arg.Is<IReadOnlyDictionary<string, object?>>(p => (Guid)p!["DeliveryId"]! == first.Id && (int)p!["Version"]! == 2), Arg.Any<CancellationToken>());
        await commands.Received(1).ExecuteAsync(WebhookDeliverySql.MarkDelivered,
            Arg.Is<IReadOnlyDictionary<string, object?>>(p => (Guid)p!["DeliveryId"]! == second.Id), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Lost_claim_never_sends_to_the_provider()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WebhookDeliveryWorkItem>(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([
                new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}", 0, 1, Guid.NewGuid(),
                    "https://93.184.216.34/hook", "secret", "event")]));
        var clients = Substitute.For<IHttpClientFactory>();
        var processor = new WebhookDeliveryBatchProcessor(query, Substitute.For<ICommandExecutor>(),
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), clients,
            new WebhookSigningSecretProtector(DataProtectionProvider.Create(nameof(WebhookDeliveryTests))),
            new FixedClock(DateTimeOffset.UtcNow), Options.Create(new DatabaseOptions()),
            Options.Create(new WebhookDeliveryWorkerOptions()), NullLogger<WebhookDeliveryBatchProcessor>.Instance);
        Assert.AreEqual(0, await processor.ProcessPendingAsync(default));
        clients.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> onSend)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            onSend(request, cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
