using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ReplayKafkaRange;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>持久重放的准入、创建重试和未知进度；真实事务及 Broker 检查在双库集成覆盖。</summary>
[TestClass]
public sealed class KafkaRangeReplayOperationsServiceTests
{
    [TestMethod]
    public async Task Disabled_replay_rejects_before_any_persistence_or_broker_call()
    {
        var f = new Fixture(false);
        var result = await f.Service.CreateAsync(f.Request, default);
        Assert.AreEqual(MessagingErrorCodes.KafkaReplayUnavailable, result.Error?.Code);
        Assert.IsEmpty(f.Query.ReceivedCalls()); Assert.IsEmpty(f.Replay.ReceivedCalls()); Assert.IsEmpty(f.Audit.Writes);
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("1e3")]
    [DataRow("9223372036854775808")]
    [DataRow(" 10")]
    public async Task Invalid_decimal_offset_is_rejected_before_persistence(string offset)
    {
        var f = new Fixture();
        var result = await f.Service.CreateAsync(f.Request with { FromOffset = offset }, default);
        Assert.AreEqual(MessagingErrorCodes.KafkaReplayRequestInvalid, result.Error?.Code);
        Assert.IsEmpty(f.Query.ReceivedCalls()); Assert.IsEmpty(f.Replay.ReceivedCalls());
    }

    [TestMethod]
    public async Task Untrusted_actor_or_blank_reason_cannot_create_operation()
    {
        var f = new Fixture();
        Assert.AreEqual("messaging.operations.reason_invalid", (await f.Service.CreateAsync(f.Request with { Reason = " " }, default)).Error?.Code);
        f.Context.HttpContext = new DefaultHttpContext();
        Assert.AreEqual("messaging.operations.actor_required", (await f.Service.CreateAsync(f.Request, default)).Error?.Code);
        Assert.IsEmpty(f.Query.ReceivedCalls());
    }

    [TestMethod]
    public async Task Deployment_budget_is_enforced_before_audit_or_broker()
    {
        var f = new Fixture();
        var result = await f.Service.CreateAsync(f.Request with { MaxMessages = 1001 }, default);
        Assert.AreEqual(MessagingErrorCodes.KafkaReplaySynchronousLimitExceeded, result.Error?.Code);
        Assert.IsEmpty(f.Audit.Writes); Assert.IsEmpty(f.Replay.ReceivedCalls());
    }

    [TestMethod]
    public async Task Creation_is_audited_but_does_not_execute_business_handler()
    {
        var f = new Fixture();
        var result = await f.Service.CreateAsync(f.Request, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(f.Id, result.Value!.OperationId);
        Assert.AreEqual(KafkaReplayOperationStates.Prepared, result.Value.State);
        Assert.AreEqual(1, f.Audit.Writes.Count);
        Assert.AreEqual(MessagingDomainAuditOutcomes.Requested, f.Audit.Writes[0].Outcome);
        Assert.AreEqual(f.Id, f.Audit.Writes[0].EntityId);
        Assert.IsEmpty(f.Replay.ReceivedCalls());
    }

    [TestMethod]
    public async Task Lost_create_response_retries_original_key_without_inserting_or_auditing_twice()
    {
        var f = new Fixture(); var row = f.Row(); f.ReturnRow(row);
        var result = await f.Service.CreateAsync(f.Request, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(row.Id, result.Value!.OperationId);
        Assert.IsEmpty(f.Command.ReceivedCalls()); Assert.IsEmpty(f.Audit.Writes);
    }

    [TestMethod]
    public async Task Reusing_creation_key_for_different_request_or_actor_is_conflict()
    {
        var f = new Fixture(); f.ReturnRow(f.Row());
        Assert.AreEqual(ErrorType.Conflict, (await f.Service.CreateAsync(f.Request with { MaxMessages = 2 }, default)).Error?.Type);
        f.ReturnRow(f.Row() with { OperatorUserId = Guid.CreateVersion7() });
        Assert.AreEqual(ErrorType.Conflict, (await f.Service.CreateAsync(f.Request, default)).Error?.Type);
        Assert.IsEmpty(f.Command.ReceivedCalls());
    }

    [TestMethod]
    public async Task Concurrent_executor_or_live_lease_stops_before_broker()
    {
        var f = new Fixture();
        f.ReturnRow(f.Row() with { State = "running", LeaseUntilUtc = f.Now.AddSeconds(30) });
        Assert.AreEqual(ErrorType.Conflict, (await f.Service.ExecuteAsync(f.Id, new("resume verified gap"), default)).Error?.Type);
        f.Lock.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IAsyncDisposable?>(null));
        Assert.AreEqual(ErrorType.Conflict, (await f.Service.ExecuteAsync(f.Id, new("resume verified gap"), default)).Error?.Type);
        Assert.IsEmpty(f.Replay.ReceivedCalls());
    }

    [TestMethod]
    public void Expired_lease_and_unconfirmed_partition_remain_unknown_without_losing_large_offset()
    {
        var f = new Fixture();
        var snapshot = new KafkaReplaySnapshot("cluster", "topic", [new(0, 9007199254740993, 9007199254740996, 9007199254740994, 9007199254740994)]);
        var row = f.Row() with { State = "running", LeaseUntilUtc = f.Now.AddSeconds(-1),
            SnapshotJson = JsonSerializer.Serialize(snapshot, MessagingJsonSerializerContext.Default.KafkaReplaySnapshot), ProcessedCount = 1 };
        var response = KafkaReplayOperationService.Map(row, f.Now);
        Assert.AreEqual("result_unknown", response.State); Assert.IsTrue(response.HasUnconfirmedMessage);
        Assert.AreEqual("9007199254740994", response.Partitions[0].NextOffset);
        Assert.AreEqual(1L, response.ConfirmedMessages);
    }

    private sealed class Fixture
    {
        public readonly Guid Id = Guid.CreateVersion7(), Actor = Guid.CreateVersion7();
        public readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        public readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        public readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        public readonly ITrackedKafkaReplayService Replay = Substitute.For<ITrackedKafkaReplayService>();
        public readonly IDatabaseSessionLock Lock = Substitute.For<IDatabaseSessionLock>();
        public readonly RecordingAuditWriter Audit = new();
        public readonly HttpContextAccessor Context;
        public readonly CreateKafkaReplayOperationRequest Request = new(Guid.CreateVersion7(), "messaging.orders.v1", null, null, "10", "12", [0], "fullnet.messaging.orders", 1, "repair verified gap");
        public readonly KafkaReplayOperationService Service;
        public Fixture(bool enabled = true)
        {
            Context = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new("sub", Actor.ToString()), new("name", "operator")], "test")) } };
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Id);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(Now);
            var catalog = Substitute.For<IIntegrationEventSubscriptionCatalog>();
            catalog.GetTopicByCodeRequired(Request.TopicCode).Returns(IntegrationEventTopicDefinition.Create(Request.TopicCode, "fullnet.messaging.order.changed", 1, EventDeliveryOwner.CdcKafka));
            var subscription = Substitute.For<IIntegrationEventSubscription>(); subscription.ConsumerName.Returns(Request.ReplayConsumerName); subscription.EventType.Returns("fullnet.messaging.order.changed"); subscription.SchemaVersion.Returns(1);
            catalog.GetAllSubscriptions().Returns([subscription]);
            Lock.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IAsyncDisposable?>(new Handle()));
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Service = new(new(Query, Command, Options.Create(new DatabaseOptions())), Replay, new(enabled, 1000, TimeSpan.FromSeconds(30)), catalog,
                Substitute.For<IEffectiveEventDeliveryOwnerResolver>(), Lock, new Transaction(), ids, clock, Audit, Substitute.For<IServiceScopeFactory>(), Context);
        }
        public KafkaReplayOperationRow Row() => new(Id, Request.RequestId, "fullnet.messaging.order.changed", 1, Request.TopicCode, Request.ReplayConsumerName,
            JsonSerializer.Serialize(Request, MessagingJsonSerializerContext.Default.CreateKafkaReplayOperationRequest), null, "prepared", 0,
            null, null, null, null, 0, 0, 0, null, Actor, "operator", Request.Reason, Now, Now);
        public void ReturnRow(KafkaReplayOperationRow row) => Query.QuerySingleOrDefaultAsync<KafkaReplayOperationRow>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
    }
    private sealed class RecordingAuditWriter : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        public List<MessagingDomainAuditWrite> Writes { get; } = [];
        public Task WriteAsync(MessagingDomainAuditWrite item, CancellationToken token) { Writes.Add(item); return Task.CompletedTask; }
    }
    private sealed class Handle : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class Transaction : ICommandTransaction { public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token) => action(token); }
}
