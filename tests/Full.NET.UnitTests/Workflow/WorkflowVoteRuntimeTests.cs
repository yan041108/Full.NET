using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow;
using Full.NET.Hosting.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>投票是人工决策屏障，个人独立表单与声明结果字段必须在发布期固定。</summary>
[TestClass]
public sealed class WorkflowVoteRuntimeTests
{
    [TestMethod]
    public async Task Vote_endpoint_requires_its_own_permission()
    {
        var builder = WebApplication.CreateBuilder(); var module = new WorkflowModule();
        module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build(); module.MapEndpoints(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/api/v1/workflow/todos/{todoId:guid}/vote");
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.HasCount(1, authorization);
        Assert.AreEqual(FullNetPermissionPolicies.For(WorkflowPermissions.TodosVote), authorization[0].Policy);
    }
    [TestMethod]
    public void Vote_satisfies_human_decision_barrier_and_preserves_frozen_policy()
    {
        var form = Guid.CreateVersion7(); var draft = Graph(form, "editable"); var schema = Schema("switch");
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(draft, schema).IsSuccess);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, schema, out var plan));
        Assert.IsTrue(plan!.TryResolveStart(out var transition)); Assert.AreEqual("vote", transition.NextHumanNodeKey);
        Assert.AreEqual(form, transition.VotePolicy!.BallotFormVersionId);
        Assert.IsNull(transition.ApprovalPolicy); Assert.IsNull(transition.TaskPolicy);
        Assert.IsTrue(plan.TryResolveAfterHuman("vote", out var end)); Assert.IsTrue(end.CompletesInstance);
    }

    [TestMethod]
    [DataRow("text", "editable")]
    [DataRow("switch", "readOnly")]
    [DataRow("switch", "hidden")]
    public void Result_binding_requires_a_writable_boolean_field(string type, string access)
    {
        var result = WorkflowDefinitionCompiler.Compile(Graph(Guid.CreateVersion7(), access), Schema(type));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.DefinitionVotePolicyInvalid, result.ErrorCode);
    }

    private static WorkflowDefinitionDraft Graph(Guid form, string access) => new(1,
    [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "vote" } })),
        new("vote", "human.vote", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" },
            fieldPolicies = new Dictionary<string, string> { ["accepted"] = access }, votePolicy = new { ballotFormVersionId = form,
                participationPercentage = 100, approvalPercentage = 67, allowAbstain = true, resultFieldKey = "accepted" } })),
        new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))]);
    private static WorkflowFormSchema Schema(string type) => new(1, 1, [new("main", [new("accepted", type, false, new Dictionary<string, JsonElement>())])]);
}
