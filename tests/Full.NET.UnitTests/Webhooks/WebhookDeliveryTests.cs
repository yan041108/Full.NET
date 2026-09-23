using System.Net;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Webhooks.Serialization;
using Full.NET.Modules.Webhooks;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
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
            ]));

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var transaction = Substitute.For<ICommandTransaction>();
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
            ]));

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var processor = new WebhookDeliveryBatchProcessor(
            query,
            command,
            Substitute.For<ICommandTransaction>(),
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
