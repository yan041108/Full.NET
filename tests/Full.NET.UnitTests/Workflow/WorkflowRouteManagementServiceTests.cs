using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageRoutes;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using NSubstitute;
using System.Text.Json;

namespace Full.NET.UnitTests.Workflow;

/// <summary>无效身份在参数绑定边界失败，不能进入持久化标识处理器或读取作用域。</summary>
[TestClass]
public sealed class WorkflowRouteManagementServiceTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("{}")]
    public async Task Missing_or_obsolete_publication_binding_cannot_resume_or_complete_the_original_route(string? binding)
    {
        var frames = WorkflowExecutionFrameScopeTests.Create(); var instance = frames.Instance with { StatusKey = "suspended" };
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        WorkflowNodeDraft Node(string key, string type, object config) => new(key, type, 1, JsonSerializer.SerializeToElement(config));
        var route = Node("route", "control.route", new { nextNodeKeys = new[] { "end" }, routePolicy = new { modeKey = "rules", targets = new[] { new { targetKey = "retry", nodeKey = "review", regionKey = "round", maximumIterations = 2,
            condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } } } } });
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "control.empty", new { nextNodeKeys = new[] { "route" } }), route, Node("end", "end", new { })], "automation");
        var schema = WorkflowDynamicRoutePolicyTests.Schema(); var form = Guid.CreateVersion7(); var id = Guid.CreateVersion7(); const string input = "{\"amount\":0}";
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, form, JsonSerializer.Serialize(draft, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft),
                JsonSerializer.Serialize(schema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema), null, "active"));
        query.QuerySingleOrDefaultAsync<WorkflowRouteDecisionRecord>(WorkflowRouteSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRouteDecisionRecord(id, instance.Id, "host", instance.Id, instance.DefinitionVersionId, "route", input, WorkflowRouteDecisionExecutor.Hash(input), 1,
                WorkflowJsonCanonicalizer.Compile(route.Config).ContentHash, "decision_unknown", null, null, null, null, null, null, null, null, 1, DateTimeOffset.UtcNow, null));
        query.QuerySingleOrDefaultAsync<string>(WorkflowDesignerAssetSql.FindBindings, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(binding);
        var service = new WorkflowRouteManagementService(query, command, new DirectTransaction(), tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Substitute.For<ITenantFeatureEntitlementPort>(), null!);
        var result = await service.ReconcileAsync(instance.Id, id, Guid.CreateVersion7(), new(instance.Revision, 1, null, "旧发布不能推进", "blocked-render"), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
        await query.Received(1).QuerySingleOrDefaultAsync<string>(WorkflowDesignerAssetSql.FindBindings, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.IsFalse(command.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], WorkflowSql.ResumeInstanceWithRevision)));
        Assert.IsFalse(command.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], WorkflowRouteSql.CompleteStep)));
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Empty_identifiers_are_validation_errors_without_querying(bool emptyRoot, bool emptyRoute)
    {
        var query = Substitute.For<IQueryExecutor>();
        var service = new WorkflowRouteManagementService(query, null!, null!, null!, null!, null!, null!, null!);
        var result = await service.GetAsync(emptyRoot ? Guid.Empty : Guid.CreateVersion7(), emptyRoute ? Guid.Empty : Guid.CreateVersion7(), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Validation, result.Error!.Type);
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }

    private sealed class DirectTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token) => action(token);
    }
}
