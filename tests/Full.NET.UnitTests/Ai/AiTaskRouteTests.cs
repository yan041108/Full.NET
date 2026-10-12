using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Features.RouteTasks;
using Full.NET.Modules.Ai.Serialization;
using System.Text.Json;
using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using TaskEndpoint = Full.NET.Modules.Ai.Features.RouteTasks.Endpoint;

namespace Full.NET.UnitTests.Ai;

/// <summary>首个封闭路由必须提供可静态序列化的明确意图契约。</summary>
[TestClass]
public sealed class AiTaskRouteTests
{
    [TestMethod]
    public void Route_red_request_has_source_generated_metadata()
    {
        var type = typeof(CreateAiAgentRunRequest).Assembly.GetType("Full.NET.Modules.Ai.Contracts.RouteAiTaskRequest");
        Assert.IsNotNull(type, "缺少封闭任务路由请求。");
        Assert.IsNotNull(Full.NET.Modules.Ai.Serialization.AiJsonSerializerContext.Default.GetTypeInfo(type));
    }

    [TestMethod]
    [DataRow("knowledge_ask")]
    [DataRow("authorized_tool_task")]
    public void Explicit_supported_intent_resolves_without_model_classification(string intent)
    {
        Assert.AreEqual(intent == "knowledge_ask" ? AiTaskIntent.KnowledgeAsk : AiTaskIntent.AuthorizedToolTask,
            AiTaskRoutePolicy.Resolve(Request(intent)));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("unknown")]
    [DataRow("Knowledge_Ask")]
    [DataRow("knowledge_ask ")]
    [DataRow("1")]
    [DataRow("delete_everything")]
    [DataRow(null)]
    public void Unknown_or_noncanonical_intent_is_rejected(string? intent)
    {
        Assert.AreEqual(AiTaskIntent.Rejected, AiTaskRoutePolicy.Resolve(Request("knowledge_ask") with { IntentKey = intent! }));
    }

    [TestMethod]
    [DataRow("knowledge_empty_session")]
    [DataRow("knowledge_no_session")]
    [DataRow("knowledge_no_payload")]
    [DataRow("knowledge_mixed")]
    [DataRow("tool_session")]
    [DataRow("tool_mixed")]
    [DataRow("tool_no_payload")]
    [DataRow("tool_prompt_version")]
    [DataRow("tool_null_prompt")]
    [DataRow("tool_single_text")]
    [DataRow("tool_remote_loop")]
    [DataRow("tool_unknown_definition")]
    public void Mixed_or_outside_first_route_payload_is_rejected(string fault)
    {
        var knowledge = Request("knowledge_ask"); var tool = Request("authorized_tool_task");
        var request = fault switch
        {
            "knowledge_empty_session" => knowledge with { SessionId = Guid.Empty },
            "knowledge_no_session" => knowledge with { SessionId = null },
            "knowledge_no_payload" => knowledge with { Knowledge = null },
            "knowledge_mixed" => knowledge with { ToolTask = tool.ToolTask },
            "tool_session" => tool with { SessionId = Guid.CreateVersion7() },
            "tool_mixed" => tool with { Knowledge = knowledge.Knowledge },
            "tool_no_payload" => tool with { ToolTask = null },
            "tool_prompt_version" => tool with { ToolTask = tool.ToolTask! with { PromptVersionId = Guid.CreateVersion7() } },
            "tool_null_prompt" => tool with { ToolTask = tool.ToolTask! with { Prompt = null! } },
            "tool_single_text" => tool with { ToolTask = tool.ToolTask! with { DefinitionKey = AiAgentRunManagementService.SingleTextDefinitionKey } },
            "tool_remote_loop" => tool with { ToolTask = tool.ToolTask! with { DefinitionKey = AiAgentRunManagementService.ReadOnlyToolLoopDefinitionKey } },
            _ => tool with { ToolTask = tool.ToolTask! with { DefinitionKey = "external-write" } },
        };
        Assert.AreEqual(AiTaskIntent.Rejected, AiTaskRoutePolicy.Resolve(request));
    }

    [TestMethod]
    public void Null_request_is_rejected() => Assert.AreEqual(AiTaskIntent.Rejected, AiTaskRoutePolicy.Resolve(null));

    [TestMethod]
    [DataRow("knowledge_ask")]
    [DataRow("authorized_tool_task")]
    public void Route_payload_roundtrips_with_source_generated_json(string intent)
    {
        var request = Request(intent);
        var json = JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.RouteAiTaskRequest);
        var restored = JsonSerializer.Deserialize(json, AiJsonSerializerContext.Default.RouteAiTaskRequest)!;
        Assert.AreEqual(request.IntentKey, restored.IntentKey);
        Assert.AreEqual(request.SessionId, restored.SessionId);
        Assert.AreEqual(request.ToolTask, restored.ToolTask);
        Assert.AreEqual(request.Knowledge?.Question, restored.Knowledge?.Question);
        if (request.Knowledge is not null) CollectionAssert.AreEqual(request.Knowledge.KnowledgeBaseIds.ToArray(), restored.Knowledge!.KnowledgeBaseIds.ToArray());
    }

    [TestMethod]
    public void First_tool_route_has_only_independent_static_workflow_tools()
    {
        var workflow = AgentWorkflowRegistry.Resolve(AgentWorkflowRegistry.ChatRenameWorkflowKey, AgentWorkflowRegistry.ChatRenameWorkflowVersion)!;
        Assert.AreEqual(2, workflow.Version);
        CollectionAssert.AreEqual(new[] { "ai.chat.sessions.list", "ai.chat.sessions.rename" },
            workflow.Nodes.Where(node => node.ToolName is not null).Select(node => node.ToolName!).ToArray());
    }

    [TestMethod]
    [DataRow("permission")]
    [DataRow("user")]
    [DataRow("session")]
    [DataRow("tenant")]
    [DataRow("scope")]
    [DataRow("unavailable")]
    public async Task Changed_authoritative_binding_never_dispatches_tool_task(string fault)
    {
        var user = Guid.CreateVersion7(); var session = Guid.CreateVersion7();
        var context = Context(user, session);
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        var authorization = Substitute.For<ICurrentSessionAuthorization>();
        var actor = new AuthorizedSessionActor(fault == "user" ? Guid.CreateVersion7() : user,
            fault == "tenant" ? Guid.CreateVersion7() : null, fault == "session" ? Guid.CreateVersion7() : session);
        authorization.AuthorizeAsync(AiAgentRunPermissions.Create, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (fault == "scope") tenant.Id.Returns(Guid.CreateVersion7());
            if (fault == "unavailable") tenant.IsHost.Returns(false);
            return Task.FromResult(fault == "permission" ? null : actor);
        });
        var result = await TaskEndpoint.ExecuteToolAsync(Request("authorized_tool_task"), null!, tenant, authorization,
            null!, context, default);
        Assert.AreEqual(403, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual("authorization.permission_denied", ((ProblemHttpResult)result).ProblemDetails.Extensions["code"]);
        await authorization.Received(1).AuthorizeAsync(AiAgentRunPermissions.Create, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("anonymous")]
    [DataRow("api_key")]
    public async Task Tool_route_requires_interactive_binding_before_authorization_or_dispatch(string fault)
    {
        var context = fault == "anonymous" ? new DefaultHttpContext() : Context(Guid.CreateVersion7(), Guid.CreateVersion7());
        if (fault == "api_key") ((ClaimsIdentity)context.User.Identity!).AddClaim(new(FullNetIdentityClaimTypes.ApiKeyId, "credential"));
        var authorization = Substitute.For<ICurrentSessionAuthorization>();
        var result = await TaskEndpoint.ExecuteToolAsync(Request("authorized_tool_task"), null!, Substitute.For<ICurrentTenant>(),
            authorization, null!, context, default);
        Assert.AreEqual(401, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual(0, authorization.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Intent_cannot_cross_endpoint_permission_boundary(bool knowledgeEndpoint)
    {
        var result = knowledgeEndpoint
            ? await TaskEndpoint.ExecuteKnowledgeAsync(Request("authorized_tool_task"), null!, null!, new DefaultHttpContext(), default)
            : await TaskEndpoint.ExecuteToolAsync(Request("knowledge_ask"), null!, null!, null!, null!, new DefaultHttpContext(), default);
        Assert.AreEqual(400, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual("ai.routing.invalid", ((ProblemHttpResult)result).ProblemDetails.Extensions["code"]);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Cancelled_route_never_enters_authorization_or_dispatch(bool knowledgeEndpoint)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            if (knowledgeEndpoint) await TaskEndpoint.ExecuteKnowledgeAsync(Request("knowledge_ask"), null!, null!, new DefaultHttpContext(), cancellation.Token);
            else await TaskEndpoint.ExecuteToolAsync(Request("authorized_tool_task"), null!, null!, null!, null!, new DefaultHttpContext(), cancellation.Token);
        });
    }

    private static DefaultHttpContext Context(Guid user, Guid session) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.ToString()),
            new Claim(FullNetIdentityClaimTypes.SessionId, session.ToString()), new Claim(FullNetIdentityClaimTypes.SecurityStamp, "stamp"),
            new Claim(FullNetIdentityClaimTypes.ActorScope, "host"), new Claim(FullNetIdentityClaimTypes.Scope, "host") }, "test")),
    };

    private static RouteAiTaskRequest Request(string intent) => intent == "knowledge_ask"
        ? new(intent, Guid.CreateVersion7(), new([Guid.CreateVersion7()], Guid.CreateVersion7(), "question"))
        : new(intent, ToolTask: new(Guid.CreateVersion7(), AiAgentRunManagementService.ChatRenameWorkflowDefinitionKey,
            Guid.CreateVersion7(), "", 100, 100));
}
