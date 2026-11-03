using System.Collections.Concurrent;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Host.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Outbox;

/// <summary>串行批次和并发单消息都恢复信封租户，成功/失败确认必须回到Host，不能污染下一条消息。</summary>
[TestClass]
public sealed class OutboxTenantContextTests
{
    [TestMethod]
    [DataRow(1, false)][DataRow(2, false)][DataRow(1, true)][DataRow(2, true)]
    public async Task Envelope_context_is_local_and_queue_acknowledgement_stays_host(int concurrency, bool fail)
    {
        var now = DateTimeOffset.UtcNow; var tenants = new Guid?[] { Guid.CreateVersion7(), null, Guid.CreateVersion7() };
        var lockId = Guid.CreateVersion7();
        var messages = tenants.Select(tenant => new OutboxEnvelope(Guid.CreateVersion7(), lockId, "fullnet.test.context", 1,
            "application/x-memorypack", tenant, null, [1], 1, now)).ToArray();
        var seen = new ConcurrentDictionary<Guid, (Guid? Tenant, bool Host)>(); var acknowledgements = new ConcurrentQueue<bool>();
        var services = new ServiceCollection(); services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenantContextWriter>(provider => provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<IOutboxStore>(provider =>
        {
            var tenant = provider.GetRequiredService<CurrentTenantAccessor>();
            var store = Substitute.For<IOutboxStore, IOutboxBacklogReader>();
            store.AcquireAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OutboxEnvelope>>(messages));
            ((IOutboxBacklogReader)store).ReadBacklogAsync(Arg.Any<CancellationToken>()).Returns(new OutboxBacklogSnapshot(0, null));
            store.MarkProcessedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { acknowledgements.Enqueue(tenant.IsHost); return Task.CompletedTask; });
            store.MarkFailedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => { acknowledgements.Enqueue(tenant.IsHost); return Task.CompletedTask; });
            return store;
        });
        services.AddScoped<IOutboxBacklogReader>(provider => (IOutboxBacklogReader)provider.GetRequiredService<IOutboxStore>());
        services.AddScoped<IIntegrationEventHandler>(provider => new ContextHandler(provider.GetRequiredService<CurrentTenantAccessor>(), seen, fail));
        await using var provider = services.BuildServiceProvider();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var processor = new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new OutboxWorkerOptions { BatchSize = 4, MaxConcurrency = concurrency }), NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessOnceAsync(default);
        Assert.HasCount(3, seen); Assert.HasCount(3, acknowledgements);
        foreach (var message in messages)
        {
            Assert.AreEqual(message.TenantId, seen[message.Id].Tenant); Assert.AreEqual(message.TenantId is null, seen[message.Id].Host);
        }
        Assert.IsTrue(acknowledgements.All(host => host));
    }

    private sealed class ContextHandler(CurrentTenantAccessor tenant, ConcurrentDictionary<Guid, (Guid? Tenant, bool Host)> seen, bool fail) : IIntegrationEventHandler
    {
        public string EventType => "fullnet.test.context";
        public int SchemaVersion => 1;
        public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token)
        { seen[context.MessageId] = (tenant.Id, tenant.IsHost); return fail ? Task.FromException(new InvalidOperationException("retry")) : Task.CompletedTask; }
        public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken token) => throw new InvalidOperationException("A trusted envelope is required.");
    }
}
