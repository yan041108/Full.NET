using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>回填使用当前输入及修订，派生审计与个人签名证据分别保存。</summary>
[TestClass]
public sealed class WorkflowResultCallbackWriterTests
{
    [TestMethod]
    public async Task Final_callback_changes_only_declared_boolean_and_records_system_source()
    {
        var fixture = Fixture();
        var result = await fixture.Writer.ApplyAsync(fixture.Instance, Guid.CreateVersion7(), "review", fixture.Plan, Values(false, "keep"), true, null, null, null, DateTimeOffset.UtcNow, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(8L, result.Value!.FormRevision);
        Assert.IsTrue(result.Value.Values["accepted"].GetBoolean()); Assert.AreEqual("keep", result.Value.Values["title"].GetString());
        var update = fixture.Commands.ReceivedCalls().Single(call => Equals(call.GetArguments()[0], WorkflowSql.UpdateFormSubmissionWithRevision));
        var args = (IReadOnlyDictionary<string, object?>)update.GetArguments()[1]!;
        Assert.AreEqual(7L, args["Revision"]); Assert.AreEqual(fixture.Instance.StartedById, args["UpdatedById"]);
        var audit = fixture.Commands.ReceivedCalls().Single(call => Equals(call.GetArguments()[0], WorkflowSql.InsertDomainAudit));
        var evidence = (IReadOnlyDictionary<string, object?>)audit.GetArguments()[1]!;
        Assert.IsNull(evidence["ActorUserId"]); Assert.AreEqual("activity.execute", evidence["OperationKey"]);
        StringAssert.Contains((string)evidence["DetailJson"]!, "result_callback");
    }
    [TestMethod]
    public async Task Stale_input_or_failed_revision_never_commits_a_derived_result()
    {
        var fixture = Fixture();
        var stale = await fixture.Writer.ApplyAsync(fixture.Instance, Guid.CreateVersion7(), "review", fixture.Plan, Values(false, "changed"), true, null, null, null, DateTimeOffset.UtcNow, default);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, stale.Error!.Code); Assert.AreEqual(0, fixture.Commands.ReceivedCalls().Count());
        fixture.Commands.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var rejected = await fixture.Writer.ApplyAsync(fixture.Instance, Guid.CreateVersion7(), "review", fixture.Plan, Values(false, "keep"), true, null, null, null, DateTimeOffset.UtcNow, default);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, rejected.Error!.Code);
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], WorkflowSql.InsertDomainAudit)));
    }
    [TestMethod]
    public async Task Already_equal_result_keeps_revision_but_records_actual_system_disposition()
    {
        var fixture = Fixture();
        var result = await fixture.Writer.ApplyAsync(fixture.Instance, Guid.CreateVersion7(), "review", fixture.Plan, Values(false, "keep"), false, null, null, null, DateTimeOffset.UtcNow, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(7L, result.Value!.FormRevision);
        Assert.AreEqual(1, fixture.Commands.ReceivedCalls().Count());
        Assert.IsTrue(Equals(fixture.Commands.ReceivedCalls().Single().GetArguments()[0], WorkflowSql.InsertDomainAudit));
    }
    private static (WorkflowResultCallbackWriter Writer, ICommandExecutor Commands, WorkflowInstanceRecord Instance, WorkflowRuntimePlan Plan) Fixture()
    {
        var now = DateTimeOffset.UtcNow; var actor = Guid.CreateVersion7();
        var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "callback.test", "one", null, "active", 1, actor, now,
            null, null, null, null, null, null);
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("accepted", "switch", false, new Dictionary<string, JsonElement>()), new("title", "text", false, new Dictionary<string, JsonElement>())])]);
        WorkflowNodeDraft Node(string key, string kind, object config) => new(key, kind, 1, JsonSerializer.SerializeToElement(config));
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, resultCallbackFieldKey = "accepted", fieldPolicies = new { accepted = "editable" } }), Node("end", "end", new { })], "automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, schema, out var plan));
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), instance.Id, instance.FormVersionId!.Value, "{\"accepted\":false,\"title\":\"keep\"}", "internal", 7, actor, now));
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        return (new(query, commands, new(query, commands), ids), commands, instance, plan!);
    }
    private static IReadOnlyDictionary<string, JsonElement> Values(bool accepted, string title) => new Dictionary<string, JsonElement>
    { ["accepted"] = JsonSerializer.SerializeToElement(accepted), ["title"] = JsonSerializer.SerializeToElement(title) };
}
