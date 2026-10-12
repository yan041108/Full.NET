using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Features.HostFileReferences;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>消费端不得确认失败副作用，重复释放和不存在的历史 Claim 则自然收敛。</summary>
[TestClass]
public sealed class DocumentFileReleaseConsumerTests
{
    private readonly IIntegrationEventSerializer serializer = new MemoryPackIntegrationEventSerializer();
    private readonly DocumentVersionDeletedIntegrationEvent message = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
    private IntegrationEventContext Context(Guid? tenant = null) => new(Guid.CreateVersion7(),
        DocumentVersionDeletedOutbox.EventType, 1, tenant, null, DateTimeOffset.UtcNow);

    [TestMethod]
    public void Deleted_version_fact_round_trips_without_reflection_serializer()
    {
        Assert.AreEqual(message, serializer.Deserialize<DocumentVersionDeletedIntegrationEvent>(serializer.Serialize(message)));
    }

    [TestMethod]
    public async Task Failure_then_retry_and_duplicate_delivery_use_same_claim_key()
    {
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        var attempts = 0;
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++attempts == 1) throw new IOException("offline");
            return Task.FromResult(Result<bool>.Success(true));
        });
        var handler = Handler(claims); var payload = serializer.Serialize(message); var context = Context();
        await Assert.ThrowsExactlyAsync<IOException>(() => handler.HandleAsync(context, payload, default));
        await handler.HandleAsync(context, payload, default);
        await handler.HandleAsync(context, payload, default);
        await claims.Received(3).ReleaseAsync(HostFileReferenceClaimIdempotencyKeys.DocumentVersion(message.VersionId), Arg.Any<CancellationToken>());
        Assert.AreEqual(IntegrationEventIdempotencyStrategy.NaturallyIdempotent, handler.IdempotencyStrategy);
    }

    [TestMethod]
    public async Task Missing_historical_claim_is_terminal_without_retry()
    {
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            Result<bool>.Failure(new Error(FilesErrorCodes.ClaimNotFound, "missing", ErrorType.NotFound))));
        await Handler(claims).HandleAsync(Context(), serializer.Serialize(message), default);
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("version")]
    [DataRow("type")]
    [DataRow("empty-id")]
    public async Task Invalid_context_or_payload_cannot_release_host_claim(string change)
    {
        var claims = Substitute.For<IHostFileReferenceClaimService>(); var context = Context(); var value = message;
        if (change == "tenant") context = Context(Guid.CreateVersion7());
        if (change == "version") context = context with { SchemaVersion = 2 };
        if (change == "type") context = context with { MessageType = "fullnet.other.deleted" };
        if (change == "empty-id") value = message with { VersionId = Guid.Empty };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Handler(claims).HandleAsync(context, serializer.Serialize(value), default));
        await claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Successful_result_with_false_value_is_not_acknowledged()
    {
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result<bool>.Success(false)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Handler(claims).HandleAsync(Context(), serializer.Serialize(message), default));
    }

    [TestMethod]
    public async Task Cancellation_is_observed_before_files_side_effect()
    {
        var claims = Substitute.For<IHostFileReferenceClaimService>(); using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Handler(claims).HandleAsync(Context(), serializer.Serialize(message), source.Token));
        await claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancellation_after_committed_release_is_not_acknowledged()
    {
        using var source = new CancellationTokenSource();
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { source.Cancel(); return Task.FromResult(Result<bool>.Success(true)); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Handler(claims).HandleAsync(Context(), serializer.Serialize(message), source.Token));
    }

    [TestMethod]
    public async Task Null_binary_payload_is_permanent_failure()
    {
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Handler(claims).HandleAsync(Context(),
            serializer.Serialize<DocumentVersionDeletedIntegrationEvent>(null!), default));
        await claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Both_api_and_worker_register_one_exact_consumer(bool worker)
    {
        var collection = new ServiceCollection().AddSingleton(serializer)
            .AddSingleton(Substitute.For<IHostFileReferenceClaimService>());
        var module = new Full.NET.Modules.Document.DocumentModule();
        var configuration = new ConfigurationBuilder().Build();
        if (worker) module.AddBackgroundServices(collection, configuration); else module.AddServices(collection, configuration);
        using var services = collection.BuildServiceProvider();
        var handler = services.GetServices<IIntegrationEventHandler>().Single(h => h.EventType == DocumentVersionDeletedOutbox.EventType);
        Assert.AreEqual(1, handler.SchemaVersion);
        Assert.AreEqual(IntegrationEventIdempotencyStrategy.NaturallyIdempotent, handler.IdempotencyStrategy);
    }

    private IIntegrationEventHandler Handler(IHostFileReferenceClaimService claims)
    {
        // 先以既有程序集发现目标类型，使缺失消费者本身成为可失败验证，避免编译失败冒充 RED。
        var type = typeof(DocumentVersionDeletedOutbox).Assembly.GetType(
            "Full.NET.Modules.Document.Features.HostFileReferences.DocumentVersionDeletedIntegrationEventHandler");
        Assert.IsNotNull(type, "A durable deletion fact requires a registered retryable consumer.");
        using var services = new ServiceCollection().AddSingleton(claims).AddSingleton(serializer).BuildServiceProvider();
        return (IIntegrationEventHandler)ActivatorUtilities.CreateInstance(services, type);
    }
}
