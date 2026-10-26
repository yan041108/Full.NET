using System.Text.Json;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>领取独立授权且不构成人工决策；并行分支不能丢失候选池的发布规则。</summary>
[TestClass]
public sealed class WorkflowClaimRuntimeTests
{
    [TestMethod]
    [DataRow("/api/v1/workflow/claim-pools", WorkflowPermissions.ClaimPoolsRead)]
    [DataRow("/api/v1/workflow/claim-pools/{poolId:guid}/claim", WorkflowPermissions.ClaimPoolsClaim)]
    [DataRow("/api/v1/workflow/claim-pools/{poolId:guid}/assign", WorkflowPermissions.ClaimPoolsAssign)]
    [DataRow("/api/v1/workflow/todos/{todoId:guid}/release", WorkflowPermissions.TodosRelease)]
    public async Task Pool_endpoints_have_separate_exact_permissions(string route, string permission)
    {
        var builder = WebApplication.CreateBuilder(); var module = new WorkflowModule(); module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build(); module.MapEndpoints(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == route);
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.HasCount(1, authorization); Assert.AreEqual(FullNetPermissionPolicies.For(permission), authorization[0].Policy);
    }

    [TestMethod]
    public void Claim_waits_without_approval_policy_and_cannot_satisfy_approval_barrier()
    {
        var graph = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "claim" } })),
             new("claim", "human.claim", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = true } })),
             new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], WorkflowKindKey: "automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(graph, out var plan));
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(graph).IsSuccess);
        Assert.IsTrue(plan!.TryResolveStart(out var transition)); Assert.AreEqual("claim", transition.NextHumanNodeKey);
        Assert.IsTrue(transition.ClaimPolicy!.AllowRelease); Assert.IsNull(transition.ApprovalPolicy); Assert.IsNull(transition.TaskPolicy);
        Assert.IsFalse(WorkflowHumanActivityKinds.IsHumanDecision("human.claim"));
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(graph with { WorkflowKindKey = "approval" }, out _));
    }

    [TestMethod]
    [DataRow("{\"allowRelease\":true,\"actorId\":\"x\"}")]
    [DataRow("{\"allowRelease\":true,\"allowRelease\":false}")]
    [DataRow("{\"allowRelease\":1}")]
    public void Claim_policy_rejects_unknown_duplicate_and_wrong_typed_properties(string json)
    {
        using var config = JsonDocument.Parse("{\"claimPolicy\":" + json + "}");
        Assert.IsFalse(WorkflowClaimPolicy.TryRead(config.RootElement, out _));
    }
}
