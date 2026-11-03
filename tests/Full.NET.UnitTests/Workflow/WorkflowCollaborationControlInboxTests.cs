using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Serialization.MemoryPack;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>消费者只记录自身信封事实，竞争重投保留第一次物理身份。</summary>
[TestClass]
public sealed class WorkflowCollaborationControlInboxTests
{
    [TestMethod]
    [DataRow("first")]
    [DataRow("replay")]
    [DataRow("race")]
    [DataRow("foreign")]
    [DataRow("oversize")]
    public async Task Authenticated_control_delivery_is_local_bounded_and_naturally_idempotent(string mode)
    {
        var data = WorkflowCollaborationControlPolicyTests.Control("created");
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>(); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var target = data.Admission.TargetTenantScopeKey == "host" ? (Guid?)null : Guid.ParseExact(data.Admission.TargetTenantScopeKey[7..], "N");
        tenant.Id.Returns(target);
        tenant.IsHost.Returns(target is null); tenant.IsAvailable.Returns(target is not null);
        var received = data.Row with { StatusKey = "received", EnvelopeMessageId = Guid.CreateVersion7(), ReceivedAtUtc = DateTimeOffset.UtcNow, Revision = 2 };
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Admission);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationControlRecord>(WorkflowCollaborationControlSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(mode == "replay" ? received : data.Row, received);
        command.ExecuteAsync(WorkflowCollaborationControlSql.Receive, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(mode == "race" ? 0 : 1);
        var handler = new WorkflowCollaborationCreatedHandler(new(new MemoryPackIntegrationEventSerializer(), query, command, tenant, clock));
        var context = new IntegrationEventContext(Guid.CreateVersion7(), handler.EventType, 1, mode == "foreign" ? Guid.CreateVersion7() : target, null, clock.UtcNow);
        var bytes = new MemoryPackIntegrationEventSerializer().Serialize(new WorkflowCollaborationControlIntegrationEvent(data.Admission.Id, data.Row.Id, data.Admission.BindingHash, data.Row.BodyHash));
        if (mode is "foreign" or "oversize")
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => handler.HandleAsync(context, mode == "oversize" ? new byte[1025] : bytes, default));
            Assert.AreEqual(0, command.ReceivedCalls().Count());
        }
        else
        {
            await handler.HandleAsync(context, bytes, default);
            Assert.AreEqual(mode == "replay" ? 0 : 1, command.ReceivedCalls().Count());
        }
    }

    [TestMethod]
    public async Task Missing_envelope_cannot_be_replaced_by_a_self_declared_control_payload()
    {
        var handler = new WorkflowCollaborationCreatedHandler(new(Substitute.For<IIntegrationEventSerializer>(), Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(), Substitute.For<ICurrentTenant>(), Substitute.For<IClock>()));
        await Assert.ThrowsAsync<InvalidDataException>(() => handler.HandleAsync(ReadOnlyMemory<byte>.Empty, default));
    }
}
