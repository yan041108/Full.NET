using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Tools;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiApprovalCurrentAdmissionTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(int.MaxValue)]
    public async Task Approval_creation_freezes_server_target_revision(int revision)
    {
        using var f = new Fixture(); f.ApprovalExists = false;
        f.Target = new() { Id = f.TargetId, OwnerUserId = f.Actor.UserId, Title = "old", Version = revision };
        var result = await ApprovalService(f).CreateAsync(CreateRequest(f), CreateBinding(f));
        Assert.IsTrue(result.IsSuccess);
        var write = f.Commands.ReceivedCalls().Single();
        var parameters = (IReadOnlyDictionary<string, object?>)write.GetArguments()[1]!;
        using var json = JsonDocument.Parse((string)parameters["PresentationJson"]!);
        Assert.IsTrue(json.RootElement.TryGetProperty("targetVersion", out var version));
        Assert.AreEqual(revision, version.GetInt32());
        Assert.AreEqual("old", json.RootElement.GetProperty("target").GetString());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task Approval_creation_rejects_unusable_target_revision(int revision)
    {
        using var f = new Fixture(); f.ApprovalExists = false;
        f.Target = new() { Id = f.TargetId, OwnerUserId = f.Actor.UserId, Title = "old", Version = revision };
        var result = await ApprovalService(f).CreateAsync(CreateRequest(f), CreateBinding(f));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Approval_creation_cancellation_after_target_query_never_writes_snapshot()
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource(); f.ApprovalExists = false;
        f.OnTargetQuery = cancellation.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => ApprovalService(f).CreateAsync(CreateRequest(f), CreateBinding(f), cancellation.Token));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Existing_approval_is_not_rebased_to_current_target_revision()
    {
        using var f = new Fixture();
        var snapshot = f.Record.PresentationJson;
        f.Target = new() { Id = f.TargetId, OwnerUserId = f.Actor.UserId, Title = "old", Version = 3 };
        var result = await ApprovalService(f).CreateAsync(CreateRequest(f), CreateBinding(f));
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(f.Record.Id, result.Value!.ApprovalId);
        Assert.AreEqual(snapshot, f.Record.PresentationJson);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Legacy_approval_remains_readable_without_target_revision_backfill()
    {
        using var f = new Fixture();
        f.Record.PresentationJson = """{"action":"Rename","target":"old","change":"new","scope":"owned","costCeiling":null,"currency":null}""";
        var snapshot = f.Record.PresentationJson;
        var result = await ApprovalService(f).GetOwnedAsync(f.Record.Id, f.Actor.UserId);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("old", result.Value!.TargetSummary);
        Assert.AreEqual(snapshot, f.Record.PresentationJson);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    private static SessionBindingSnapshot CreateBinding(Fixture f) =>
        new(f.Actor.UserId, null, f.Actor.SessionId, "stamp", "host", "host");

    private static CreateAiAgentApprovalRequest CreateRequest(Fixture f) =>
        new(f.RunId, f.Record.OperationId, RenameChatSessionToolHandler.ToolName, 1, f.Invocation.Arguments.GetRawText());

    private static AiAgentApprovalService ApprovalService(Fixture f)
    {
        var transaction = new RecordingTransaction(); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7());
        f.Queries.RunQuery = () => new() { Id = f.RunId, ScopeKey = "host", ActorUserId = f.Actor.UserId, SessionId = f.Actor.SessionId };
        var runs = new AiAgentRunStore(f.Queries, f.Commands, transaction, ids, f.Clock, Options.Create(new DatabaseOptions()));
        var sessions = new AiChatSessionQueryService(f.Queries, f.Tenant, Options.Create(new DatabaseOptions()), f.Clock);
        var rename = new RenameChatSessionToolHandler(null!, sessions, f.Consumption, transaction, f.Tenant);
        var remote = Substitute.For<IRemoteAgentToolDefinitionSource>();
        remote.BuildDefinitionsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<AgentToolDefinition>());
        var tools = new AgentToolRegistryFactory(new PingToolHandler(), null!, null!, rename, remote);
        return new AiAgentApprovalService(f.Queries, f.Commands, runs, sessions, null!, tools, f.Consumption, f.Gate,
            null!, f.Tenant, f.Clock, ids, Options.Create(new AiAgentRuntimeOptions()));
    }
}
