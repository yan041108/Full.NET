using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Serialization.MemoryPack;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>真实MemoryPack消息只证明本地源收件，不允许载荷自报目标租户或借重投创建第二份请求。</summary>
[TestClass]
public sealed class WorkflowCollaborationRequestedHandlerTests
{
    private static (WorkflowCollaborationCallRecord Call, WorkflowCollaborationRequestedIntegrationEvent Event) Request()
    {
        var id = Guid.CreateVersion7(); var delivery = Guid.CreateVersion7(); var instance = Guid.CreateVersion7(); var grant = Guid.CreateVersion7(); var target = Guid.CreateVersion7();
        const string input = "{\"title\":\"shared\",\"secret\":\"private\"}", projected = "{\"subject\":\"shared\"}";
        var binding = new WorkflowCollaborationCallBinding(id, delivery, instance, Guid.CreateVersion7(), Guid.CreateVersion7(), instance,
            "host", "tenant:" + target.ToString("N"), null, target, "collaborate", "end", null, null, 1,
            WorkflowCollaborationCallPolicy.Hash(input), grant, 2, new('a', 64), Guid.CreateVersion7(), new('b', 64), 32, 1, WorkflowCollaborationCallPolicy.Hash(projected));
        var json = JsonSerializer.Serialize(binding, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding);
        var hash = WorkflowCollaborationCallPolicy.Hash(json);
        var call = new WorkflowCollaborationCallRecord(id, instance, instance, "host", "collaborate", "end", grant, delivery, json, hash,
            input, binding.InputHash, 1, projected, binding.ProjectionHash, 32, "requested", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return (call, new(id, delivery, hash));
    }

    private static IntegrationEventContext Context(Guid? tenant = null) => new(Guid.CreateVersion7(), WorkflowCollaborationIntegrationEventTypes.Requested, 1, tenant, null, DateTimeOffset.UtcNow);
    private static WorkflowCollaborationRequestedHandler Handler(IQueryExecutor query, ICommandExecutor command)
    {
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        return new(new MemoryPackIntegrationEventSerializer(), query, command, clock, tenant);
    }

    [TestMethod]
    public async Task First_delivery_records_only_the_envelope_scope_without_starting_target_work()
    {
        var request = Request(); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(request.Call);
        command.ExecuteAsync(WorkflowCollaborationCallSql.InsertDelivery, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        await Handler(query, command).HandleAsync(Context(), new MemoryPackIntegrationEventSerializer().Serialize(request.Event), default);
        await command.Received(1).ExecuteAsync(WorkflowCollaborationCallSql.InsertDelivery,
            Arg.Is<object?>(value => value is Dictionary<string, object?> && ((Dictionary<string, object?>)value)["TenantScopeKey"]!.Equals("host")), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Replayed_logical_request_preserves_first_physical_receipt()
    {
        var request = Request(); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(request.Call);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationDeliveryRecord>(WorkflowCollaborationCallSql.FindDelivery, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowCollaborationDeliveryRecord(request.Event.DeliveryId, request.Call.Id, "host", Guid.CreateVersion7(), request.Call.BindingHash, "received", 1, DateTimeOffset.UtcNow));
        var handler = Handler(query, command); var bytes = new MemoryPackIntegrationEventSerializer().Serialize(request.Event);
        await handler.HandleAsync(Context(), bytes, default); await handler.HandleAsync(Context(), bytes, default);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Foreign_envelope_cannot_claim_a_real_host_request()
    {
        var request = Request(); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(request.Call);
        await Assert.ThrowsAsync<InvalidDataException>(() => Handler(query, command).HandleAsync(Context(Guid.CreateVersion7()), new MemoryPackIntegrationEventSerializer().Serialize(request.Event), default));
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("binding")]
    [DataRow("input")]
    [DataRow("projection")]
    [DataRow("extra")]
    [DataRow("duplicate")]
    public async Task Changed_persisted_proof_or_projection_cannot_be_received(string part)
    {
        var request = Request(); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var changed = part switch { "binding" => request.Call with { BindingJson = request.Call.BindingJson + " " },
            "input" => request.Call with { InputJson = "{}" }, _ => request.Call with { ProjectionJson = "{}" } };
        var message = request.Event;
        if (part is "extra" or "duplicate")
        {
            var json = request.Call.BindingJson.Insert(1, part == "extra" ? "\"tenantOverride\":\"other\"," : "\"grantRevision\":2,");
            var hash = WorkflowCollaborationCallPolicy.Hash(json);
            changed = request.Call with { BindingJson = json, BindingHash = hash };
            message = request.Event with { BindingHash = hash };
        }
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(changed);
        await Assert.ThrowsAsync<InvalidDataException>(() => Handler(query, command).HandleAsync(Context(), new MemoryPackIntegrationEventSerializer().Serialize(message), default));
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_trusted_envelope_is_rejected()
    {
        var handler = Handler(Substitute.For<IQueryExecutor>(), Substitute.For<ICommandExecutor>());
        await Assert.ThrowsAsync<InvalidDataException>(() => handler.HandleAsync(ReadOnlyMemory<byte>.Empty, default));
    }

    [TestMethod]
    public async Task Preparing_call_cannot_be_received_before_capabilities_and_request_are_frozen()
    {
        var request = Request(); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(request.Call with { StatusKey = "preparing" });
        await Assert.ThrowsAsync<InvalidDataException>(() => Handler(query, command).HandleAsync(Context(), new MemoryPackIntegrationEventSerializer().Serialize(request.Event), default));
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }
}
