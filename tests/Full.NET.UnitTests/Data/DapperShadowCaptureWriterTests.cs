using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

/// <summary>采集的默认关闭开销与信封复用，不能因为试点增加全局序列化或正式追加写入。</summary>
[TestClass]
public sealed class DapperShadowCaptureWriterTests
{
    [TestMethod]
    [DataRow(true, 256, true)]
    [DataRow(true, 257, false)]
    [DataRow(false, 257, true)]
    public void Capture_policy_respects_the_persisted_stream_length_when_enabled(bool enabled, int length, bool accepted)
    {
        const string prefix = "fullnet.messaging.capture.";
        var policy = new ShadowCaptureOptions { Enabled = enabled, EventType = prefix + new string('a', length - prefix.Length) };
        Assert.AreEqual(accepted, new ShadowCaptureOptionsValidator().Validate(null, policy).Succeeded);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Legacy_capture_serializes_once_and_only_matching_enabled_stream_adds_one_command(bool enabled, bool matches)
    {
        const string type = "fullnet.messaging.capture.test";
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<SqlStatement>()!.Name == "messaging.shadow_capture.reserve_and_insert" ? 2 : 1);
        var serializer = Substitute.For<IIntegrationEventSerializer>(); serializer.ContentType.Returns("application/x-memorypack");
        var payload = new object(); var bytes = new byte[] { 1, 2, 3 }; serializer.Serialize(payload).Returns(bytes);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7());
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns(Guid.CreateVersion7());
        var resolver = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
        resolver.ResolveDeliveryOwner(type, 1, Arg.Any<EventDeliveryOwner?>()).Returns(EventDeliveryOwner.LegacyPolling);
        var gate = Substitute.For<IEventStreamOwnershipGate>();
        var capture = new DapperShadowCaptureWriter(command, clock, Options.Create(new ShadowCaptureOptions
            { Enabled = enabled, EventType = matches ? type : "fullnet.messaging.other", SchemaVersion = 1 }));
        var routed = new DapperRoutedOutboxWriter(new(command, serializer, ids, tenant, clock),
            new(command, serializer, ids, tenant, clock), resolver, gate, capture);
        await routed.AddAsync(type, 1, payload, IntegrationEventMetadata.Create("key", "fullnet.messaging.tests"));
        serializer.Received(1).Serialize(payload);
        var calls = command.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ICommandExecutor.ExecuteAsync)).ToArray();
        Assert.AreEqual(enabled && matches ? 2 : 1, calls.Length);
        Assert.IsFalse(calls.Any(call => ((SqlStatement)call.GetArguments()[0]!).Name.StartsWith("messaging.outbox_event", StringComparison.Ordinal)));
        if (enabled && matches)
        {
            var legacy = (OutboxMessage)calls[0].GetArguments()[1]!;
            var shadow = (IReadOnlyDictionary<string, object?>)calls[1].GetArguments()[1]!;
            Assert.AreEqual(legacy.Id, shadow["Id"]); Assert.AreSame(legacy.Payload, shadow["Payload"]);
            Assert.AreEqual(legacy.TenantId, shadow["TenantId"]); Assert.AreEqual(legacy.OccurredAtUtc, shadow["OccurredAtUtc"]);
        }
    }
}
