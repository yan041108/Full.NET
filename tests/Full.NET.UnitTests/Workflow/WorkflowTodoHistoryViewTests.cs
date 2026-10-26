using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>本人历史待办只展示那次活动的输入输出；缺失历史不能借用未来主表单。</summary>
[TestClass]
public sealed class WorkflowTodoHistoryViewTests
{
    [TestMethod]
    [DataRow(true, true, "own-output", false)]
    [DataRow(true, false, "seen-input", false)]
    [DataRow(false, false, null, false)]
    [DataRow(true, true, "own-output", true)]
    [DataRow(true, false, "seen-input", true)]
    [DataRow(false, false, null, true)]
    public async Task Completed_todo_never_borrows_future_main_submission(bool hasView, bool hasOutput, string? expected, bool supplemental)
    {
        var actor = Guid.CreateVersion7(); var instanceId = Guid.CreateVersion7(); var version = Guid.CreateVersion7();
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), actor, "completed", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, "approve", 2, "approval", 2, null, null, null, null, null, null);
        var instance = new WorkflowInstanceRecord(instanceId, null, "host", "host", Guid.CreateVersion7(), version, "test", "one",
            null, "active", 2, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(todo);
        if (supplemental) query.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowCountersignItemContextRecord(
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, actor, todo.Id, "completed", "before", Guid.CreateVersion7(), instanceId, todo.StepId, "completed"));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, version,
                """{"schemaVersion":1,"nodes":[{"nodeKey":"approval","nodeTypeKey":"human.approval","nodeSchemaVersion":1,"config":{}}]}""",
                """{"schemaVersion":1,"adapterVersion":1,"sections":[{"sectionKey":"main","fields":[{"fieldKey":"document","fieldTypeKey":"text","required":false,"constraints":{}}]}]}""", null, "active"));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), instanceId, version, """{"document":"future-secret"}""", "Internal", 5, actor, DateTimeOffset.UtcNow));
        if (hasView) query.QuerySingleOrDefaultAsync<WorkflowTodoViewRecord>(WorkflowTodoViewSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new WorkflowTodoViewRecord("""{"document":"seen-input"}""", 1, hasOutput ? """{"document":"own-output"}""" : null, hasOutput ? 2 : null));
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var ids = Substitute.For<IIdGenerator>(); var outbox = Substitute.For<IOutboxWriter>();
        var service = new WorkflowTodoManagementService(query, command, Substitute.For<ICommandTransaction>(), tenant,
            Substitute.For<IClock>(), ids, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            new WorkflowHumanActivityPreparation(WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(), query, WorkflowTodoManagementTestDependencies.CreateStarterSelectionPreparation()),
            new WorkflowNotificationOutboxPublisher(outbox), WorkflowTodoManagementTestDependencies.CreateCountersignService(query, command, tenant),
            WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateParallelJoinCoordinator(query, command), WorkflowTodoManagementTestDependencies.CreateAttachmentCoordinator(),
            new WorkflowBranchStateCoordinator(query, command), WorkflowTodoManagementTestDependencies.CreateSignatureCoordinator(query, command),
            WorkflowTodoManagementTestDependencies.CreateStepResolution(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateTaskDecisions(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateResultCallbacks(query, command, ids));
        var result = await service.GetAsync(todo.Id, actor);
        Assert.AreEqual(hasView, result.IsSuccess);
        if (hasView)
        {
            Assert.AreEqual(expected, result.Value!.Submission.GetProperty("document").GetString());
            if (supplemental) CollectionAssert.AreEquivalent(new[] { "approve", "reject", "remind" }, result.Value.AllowedActions.ToArray());
            else Assert.Contains("return", result.Value.AllowedActions);
        }
        else Assert.AreEqual(WorkflowErrorCodes.TodoNotActive, result.Error?.Code);
    }
}
