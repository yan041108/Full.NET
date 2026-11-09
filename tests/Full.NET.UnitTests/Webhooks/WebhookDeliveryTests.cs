using Full.NET.Abstractions.Ids;
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
    public void Production_payload_matches_the_shared_receiver_wire_fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) directory = directory.Parent;
        Assert.IsNotNull(directory, "Repository root was not found.");
        var fixture = File.ReadAllText(Path.Combine(directory.FullName, "contracts", "webhooks", "workflow-instance-completed-v1.json"));
        var envelope = JsonSerializer.Deserialize(fixture, WebhookPayloadJsonSerializerContext.Default.WebhookEventEnvelope);
        Assert.IsNotNull(envelope);
        Assert.AreEqual("fullnet.workflow.instance.completed", envelope.EventType);
        Assert.AreEqual(Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf298"), envelope.EventId);
        Assert.AreEqual("private-business-reference", envelope.Data.BusinessId);
        var actual = JsonSerializer.Serialize(envelope, WebhookPayloadJsonSerializerContext.Default.WebhookEventEnvelope);
        Assert.IsTrue(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(fixture), System.Text.Json.Nodes.JsonNode.Parse(actual)), "Producer and receiver wire formats must match.");
    }

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
        Assert.IsTrue(services.Any(descriptor=>descriptor.ServiceType==typeof(WebhookEventEnqueueService)));
        Assert.AreEqual(1,services.Count(descriptor=>descriptor.ServiceType==typeof(Full.NET.Modules.Workflow.Contracts.IWorkflowInstanceCompletedSink)
            &&descriptor.ImplementationType==typeof(WorkflowInstanceCompletedWebhookSink)));
        Assert.IsFalse(services.Any(descriptor=>descriptor.ServiceType==typeof(IIntegrationEventHandler)));
    }

    [TestMethod]
    [DataRow(false,false)]
    [DataRow(false,true)]
    [DataRow(true,false)]
    [DataRow(true,true)]
    public async Task Workflow_completion_sink_owns_local_queue_transaction_and_restores_original_scope(bool host,bool failure)
    {
        var tenantId=Guid.CreateVersion7();var tenant=new CurrentTenantAccessor();if(host)tenant.SetHost();else tenant.SetTenant(new TenantContext(tenantId,"acme","Acme"));
        var query=Substitute.For<IQueryExecutor>();query.QueryAsync<Guid>(Arg.Any<SqlStatement>(),Arg.Any<IReadOnlyDictionary<string,object?>>(),Arg.Any<CancellationToken>()).Returns(call=>
        {Assert.IsTrue(tenant.IsHost);Assert.AreEqual(tenantId,((IReadOnlyDictionary<string,object?>)call[1]!)["TenantId"]);return Task.FromResult<IReadOnlyList<Guid>>([Guid.CreateVersion7()]);});
        var commands=Substitute.For<ICommandExecutor>();commands.ExecuteAsync(Arg.Any<SqlStatement>(),Arg.Any<IReadOnlyDictionary<string,object?>>(),Arg.Any<CancellationToken>())
            .Returns(_=>failure?Task.FromException<int>(new InvalidOperationException("test queue failure")):Task.FromResult(1));
        var now=DateTimeOffset.UtcNow;var enqueue=new WebhookEventEnqueueService(query,commands,new FixedClock(now),NewIds());
        var sink=new WorkflowInstanceCompletedWebhookSink(enqueue,new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),tenant);
        var context=new IntegrationEventContext(Guid.CreateVersion7(),Full.NET.Modules.Workflow.Contracts.WorkflowNotificationIntegrationEventTypes.InstanceCompleted,1,tenantId,null,now);
        var completed=new Full.NET.Modules.Workflow.Contracts.WorkflowInstanceCompletedIntegrationEvent(Guid.CreateVersion7(),Guid.CreateVersion7(),"test","business",now);
        if(failure)await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>sink.HandleAsync(context,completed,default));else await sink.HandleAsync(context,completed,default);
        Assert.AreEqual(host,tenant.IsHost);if(!host){Assert.AreEqual(tenantId,tenant.Id);Assert.AreEqual("acme",tenant.Identifier);Assert.AreEqual("Acme",tenant.Name);}
    }

    [TestMethod]
    public async Task Workflow_completion_sink_rejects_empty_or_mismatched_tenant_and_ignores_host_business()
    {
        var tenant=new CurrentTenantAccessor();tenant.SetTenant(new TenantContext(Guid.CreateVersion7(),"other","Other"));var query=Substitute.For<IQueryExecutor>();
        var enqueue=new WebhookEventEnqueueService(query,Substitute.For<ICommandExecutor>(),new FixedClock(DateTimeOffset.UtcNow),NewIds());
        var sink=new WorkflowInstanceCompletedWebhookSink(enqueue,new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),tenant);
        var completed=new Full.NET.Modules.Workflow.Contracts.WorkflowInstanceCompletedIntegrationEvent(Guid.CreateVersion7(),Guid.CreateVersion7(),"test","business",DateTimeOffset.UtcNow);
        foreach(var invalid in new[]{Guid.Empty,Guid.CreateVersion7()})await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>sink.HandleAsync(new(Guid.CreateVersion7(),Full.NET.Modules.Workflow.Contracts.WorkflowNotificationIntegrationEventTypes.InstanceCompleted,1,invalid,null,DateTimeOffset.UtcNow),completed,default));
        await sink.HandleAsync(new(Guid.CreateVersion7(),Full.NET.Modules.Workflow.Contracts.WorkflowNotificationIntegrationEventTypes.InstanceCompleted,1,null,null,DateTimeOffset.UtcNow),completed,default);
        await query.DidNotReceive().QueryAsync<Guid>(Arg.Any<SqlStatement>(),Arg.Any<IReadOnlyDictionary<string,object?>>(),Arg.Any<CancellationToken>());
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
            NullLogger<WebhookDeliveryBatchProcessor>.Instance, NewIds(), ActiveTenants());
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
    public void ComputeSignature_binds_v1_metadata_and_the_shared_cross_language_vector()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "contracts", "webhooks", "signature-v1.vector.json")));
        var vector = document.RootElement; var payload = vector.GetProperty("payload").GetString()!; var secret = vector.GetProperty("secret").GetString()!;
        var stamp = new WebhookSignatureStamp(vector.GetProperty("keyId").GetGuid(), vector.GetProperty("eventId").GetGuid(), vector.GetProperty("deliveryId").GetGuid(),
            vector.GetProperty("eventType").GetString()!, long.Parse(vector.GetProperty("timestamp").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        var signature = WebhookSignatureHelper.ComputeSignature(secret, payload, stamp);
        Assert.AreEqual(vector.GetProperty("signature").GetString(), signature);
        Assert.AreEqual(vector.GetProperty("payloadDigest").GetString(), WebhookSignatureHelper.ComputePayloadDigest(payload));
        foreach (var changed in new[] { stamp with { KeyId = Guid.CreateVersion7() }, stamp with { EventId = Guid.CreateVersion7() },
            stamp with { DeliveryId = Guid.CreateVersion7() }, stamp with { Timestamp = stamp.Timestamp + 1 }, stamp with { EventType = "other" } })
            Assert.AreNotEqual(signature, WebhookSignatureHelper.ComputeSignature(secret, payload, changed));
        Assert.AreNotEqual(signature, WebhookSignatureHelper.ComputeSignature(secret, payload + " ", stamp));
        Assert.ThrowsExactly<ArgumentException>(() => WebhookSignatureHelper.ComputeSignature(secret,payload,stamp with { KeyId=Guid.Empty }));
        Assert.ThrowsExactly<ArgumentException>(() => WebhookSignatureHelper.ComputeSignature(secret,payload,stamp with { EventType="bad\nevent" }));
    }

    [TestMethod]
    public void SigningSecretProtector_roundtrips_secret()
    {
        var provider = DataProtectionProvider.Create(nameof(WebhookDeliveryTests));
        var protector = new WebhookSigningSecretProtector(provider);
        var protectedValue = protector.Protect("demo-secret");
        Assert.AreEqual("demo-secret", protector.Unprotect(protectedValue));
        var fingerprint=protector.ProtectFingerprint("demo-secret");
        Assert.IsTrue(protector.MatchesFingerprint("demo-secret",fingerprint));
        Assert.IsFalse(protector.MatchesFingerprint("different-secret",fingerprint));
        Assert.AreNotEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("demo-secret"))),fingerprint);
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
            Assert.AreEqual("1", request.Headers.GetValues("X-FullNet-Signature-Version").Single());
            Assert.AreEqual(subscriptionId.ToString("D"), request.Headers.GetValues("X-FullNet-Key-Id").Single());
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
                    "fullnet.workflow.instance.completed", subscriptionId),
            ]), Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([]));

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        query.QuerySingleOrDefaultAsync<WebhookSigningProfileRecord>(Arg.Any<SqlStatement>(),Arg.Any<IReadOnlyDictionary<string,object?>>(),Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WebhookSigningProfileRecord?>(new(subscriptionId,protectedSecret)));
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
            NullLogger<WebhookDeliveryBatchProcessor>.Instance, NewIds(), ActiveTenants());

        var processed = await processor.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(1, processed);
        Assert.AreEqual(
            WebhookSignatureHelper.ComputeSignature(secret, payload, new(subscriptionId,eventId,deliveryId,"fullnet.workflow.instance.completed",DateTimeOffset.Parse("2026-09-17T00:00:00Z").ToUnixTimeSeconds())),
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
                    "fullnet.workflow.instance.completed", Guid.CreateVersion7()),
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
            NullLogger<WebhookDeliveryBatchProcessor>.Instance, NewIds(), ActiveTenants());

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
            Guid.NewGuid(), "https://93.184.216.34/hook", protector.Protect("secret"), "event", Guid.CreateVersion7());
        var second = first with { Id = Guid.NewGuid() };
        query.QuerySingleOrDefaultAsync<WebhookSigningProfileRecord>(Arg.Any<SqlStatement>(),Arg.Any<IReadOnlyDictionary<string,object?>>(),Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WebhookSigningProfileRecord?>(new(first.SigningKeyId,first.SigningSecretHash)));
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
            Options.Create(new WebhookDeliveryWorkerOptions()), NullLogger<WebhookDeliveryBatchProcessor>.Instance, NewIds(), ActiveTenants());
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
                    "https://93.184.216.34/hook", "secret", "event", Guid.CreateVersion7())]));
        var clients = Substitute.For<IHttpClientFactory>();
        var processor = new WebhookDeliveryBatchProcessor(query, Substitute.For<ICommandExecutor>(),
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), clients,
            new WebhookSigningSecretProtector(DataProtectionProvider.Create(nameof(WebhookDeliveryTests))),
            new FixedClock(DateTimeOffset.UtcNow), Options.Create(new DatabaseOptions()),
            Options.Create(new WebhookDeliveryWorkerOptions()), NullLogger<WebhookDeliveryBatchProcessor>.Instance, NewIds(), ActiveTenants());
        Assert.AreEqual(0, await processor.ProcessPendingAsync(default));
        clients.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [TestMethod]
    public async Task Inactive_or_unavailable_tenant_defers_but_closed_tenant_ends_without_channel_attempt()
    {
        foreach (var code in new[] { WebhookDeliveryBatchProcessor.TenantInactive, WebhookDeliveryBatchProcessor.TenantStateUnavailable,
            WebhookDeliveryBatchProcessor.TenantClosed })
        {
            var isClosed = code == WebhookDeliveryBatchProcessor.TenantClosed;
            var now = DateTimeOffset.UtcNow;
            var item = new WebhookDeliveryWorkItem(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "{}",
                4, 1, Guid.CreateVersion7(), "https://93.184.216.34/hooks", "unused", "test.event", Guid.CreateVersion7());
            var query = Substitute.For<IQueryExecutor>();
            query.QueryAsync<WebhookDeliveryWorkItem>(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([item]),
                Task.FromResult<IReadOnlyList<WebhookDeliveryWorkItem>>([]));
            query.QuerySingleOrDefaultAsync<WebhookSigningProfileRecord>(WebhookDeliverySql.FindSigningProfile,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
                .Returns(new WebhookSigningProfileRecord(item.SigningKeyId, "unused"));
            var directory = Substitute.For<ITenantActivityReadPort>();
            directory.ReadActivityAsync(item.TenantId, Arg.Any<CancellationToken>()).Returns(
                code == WebhookDeliveryBatchProcessor.TenantStateUnavailable
                    ? Task.FromException<TenantActivityState>(new TimeoutException())
                    : Task.FromResult(isClosed ? TenantActivityState.Closed : TenantActivityState.Inactive));
            var commands = Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>()).Returns(1);
            var clients = Substitute.For<IHttpClientFactory>();
            var processor = new WebhookDeliveryBatchProcessor(query, commands,
                new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), clients,
                new WebhookSigningSecretProtector(DataProtectionProvider.Create(nameof(WebhookDeliveryTests))),
                new FixedClock(now), Options.Create(new DatabaseOptions()),
                Options.Create(new WebhookDeliveryWorkerOptions { MaxAttempts = 5 }),
                NullLogger<WebhookDeliveryBatchProcessor>.Instance, NewIds(), directory);

            Assert.AreEqual(1, await processor.ProcessPendingAsync(default));
            clients.DidNotReceive().CreateClient(Arg.Any<string>());
            await commands.DidNotReceive().ExecuteAsync(WebhookDeliverySql.MarkFailed,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>());
            await commands.Received(1).ExecuteAsync(WebhookDeliverySql.Defer,
                Arg.Is<IReadOnlyDictionary<string, object?>>(p => p != null
                    && (Guid)p["DeliveryId"]! == item.Id
                    && (string)p["ErrorCode"]! == code
                    && (string)p["Status"]! == (isClosed ? WebhookDeliveryStatuses.Failed : WebhookDeliveryStatuses.Pending)
                    && (isClosed ? p["NextAttemptAtUtc"] == null : (DateTimeOffset)p["NextAttemptAtUtc"]! > now)),
                Arg.Any<CancellationToken>());
        }
    }

    private static ITenantActivityReadPort ActiveTenants()
    {
        var directory = Substitute.For<ITenantActivityReadPort>();
        directory.ReadActivityAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TenantActivityState.Active);
        return directory;
    }

    private static IIdGenerator NewIds() { var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7()); return ids; }

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
