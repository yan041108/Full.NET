using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>父级系统取消必须有同作用域真实关联和关闭事实，普通业务ID及失配子根不能构成取消权限。</summary>
[TestClass]
public sealed class WorkflowSubprocessCancellationGuardTests
{
    [TestMethod]
    [DataRow("cancelled", "active", true)]
    [DataRow("rejected", "active", true)]
    [DataRow("active", "cancelled", true)]
    [DataRow("active", "active", false)]
    public async Task Cancellation_requires_committed_parent_or_step_closure(string parentStatus, string stepStatus, bool expected)
    {
        var f = new Fixture(parentStatus, stepStatus);
        Assert.AreEqual(expected, await WorkflowSubprocessCancellationGuard.AllowsAsync(f.Child, f.Context, new(null, "host", "host"), f.Query, default));
    }
    [TestMethod]
    public async Task Missing_step_and_wrong_child_identity_are_denied()
    {
        var f = new Fixture("cancelled", "active");
        f.Query.QuerySingleOrDefaultAsync<string>(WorkflowSubprocessSql.StepStatus, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        Assert.IsFalse(await WorkflowSubprocessCancellationGuard.AllowsAsync(f.Child, f.Context, new(null, "host", "host"), f.Query, default));
        Assert.IsFalse(await WorkflowSubprocessCancellationGuard.AllowsAsync(f.Child with { Id = Guid.CreateVersion7() }, f.Context, new(null, "host", "host"), f.Query, default));
    }
    [TestMethod]
    public async Task Wrong_tenant_and_empty_context_never_query_a_foreign_root()
    {
        var f = new Fixture("cancelled", "active");
        Assert.IsFalse(await WorkflowSubprocessCancellationGuard.AllowsAsync(f.Child, f.Context, new(Guid.CreateVersion7(), "tenant", "tenant:other"), f.Query, default));
        Assert.IsFalse(await WorkflowSubprocessCancellationGuard.AllowsAsync(f.Child, f.Context with { ParentInstanceId = Guid.Empty }, new(null, "host", "host"), f.Query, default));
        Assert.AreEqual(0, f.Query.ReceivedCalls().Count());
    }
    private sealed class Fixture
    {
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public WorkflowInstanceRecord Child { get; }
        public WorkflowChildStartContext Context { get; }
        public Fixture(string parentStatus, string stepStatus)
        {
            var now = DateTimeOffset.UtcNow; var parentId = Guid.CreateVersion7(); var callId = Guid.CreateVersion7(); var itemId = Guid.CreateVersion7();
            var childId = Guid.CreateVersion7(); var starter = Guid.CreateVersion7(); var definition = Guid.CreateVersion7(); var form = Guid.CreateVersion7(); var hash = new string('a',64);
            Context = new(parentId, callId, itemId, hash);
            Child = new(childId, null, "host", "host", definition, form, WorkflowChildStartContext.BusinessType, itemId.ToString("D"), null,
                "active", 1, starter, now, null, null, null, null, null, null);
            var parent = new WorkflowInstanceRecord(parentId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "order", "request", null,
                parentStatus, 3, starter, now, null, null, null, null, null, null);
            var call = new WorkflowSubprocessCallRecord(callId, parentId, "host", "child", "end", definition, form, hash, hash, "{}", "{}", 1,
                null, null, 1, 64, "running", false, null, 1, now, now, parentId);
            var item = new WorkflowSubprocessItemRecord(itemId, callId, parentId, "host", "single", 1, starter, "{}", hash, childId, "running", null, null, null, 1, now, now);
            Query.QuerySingleOrDefaultAsync<WorkflowSubprocessCallRecord>(WorkflowSubprocessSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call);
            Query.QuerySingleOrDefaultAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.FindItem, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(item);
            Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(parent);
            Query.QuerySingleOrDefaultAsync<string>(WorkflowSubprocessSql.StepStatus, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(stepStatus);
        }
    }
}
