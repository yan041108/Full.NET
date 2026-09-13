using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Extensions;

namespace Full.NET.UnitTests.Ai;

/// <summary>固定参数和真实 Handler 输出必须符合公开 Schema。</summary>
[TestClass]
public sealed class AiToolHandlerTests
{
    [TestMethod]
    [DataRow("{}", true)]
    [DataRow("{\"page\":2,\"pageSize\":100}", true)]
    [DataRow("{\"page\":0}", false)]
    [DataRow("{\"page\":1000001}", false)]
    [DataRow("{\"pageSize\":101}", false)]
    [DataRow("{\"page\":\"1\"}", false)]
    [DataRow("{\"page\":1,\"page\":2}", false)]
    [DataRow("{\"tenantId\":\"other\"}", false)]
    [DataRow("{\"actorUserId\":\"other\"}", false)]
    [DataRow("{\"skipApproval\":true}", false)]
    [DataRow("[]", false)]
    public void Static_arguments_reject_identity_overrides_and_ambiguous_json(string json, bool allowed)
    {
        using var document = JsonDocument.Parse(json);
        if (json == "{}")
        {
            var parsed = document.RootElement.Deserialize(Full.NET.Modules.Ai.Serialization.AiToolJsonSerializerContext.Default.ToolPageArguments)!;
            Assert.AreEqual(1, parsed.Page);
            Assert.AreEqual(20, parsed.PageSize);
        }
        Assert.AreEqual(allowed, ToolArguments.Parse(document.RootElement) is not null);
    }

    [TestMethod]
    public async Task Session_handler_uses_actor_owner_and_emits_declared_items_async()
    {
        var actor = new ToolActor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(actor.TenantId!.Value, "tenant", "租户"));
        var queries = Substitute.For<IQueryExecutor>();
        queries.ReturnsForAll<Task<IReadOnlyList<AiChatSessionRecord>>>(Task.FromResult<IReadOnlyList<AiChatSessionRecord>>([]));
        var handler = new ListChatSessionsToolHandler(new AiChatSessionQueryService(queries, tenant,
            Options.Create(new DatabaseOptions()), Substitute.For<IClock>()), tenant);
        using var document = JsonDocument.Parse("{}");
        var invocation = new ToolInvocation(Guid.CreateVersion7(), null, "ai.chat.sessions.list", 1, document.RootElement);
        var result = await handler.ExecuteAsync(invocation, actor, default);
        Assert.IsTrue(result.TryGetProperty("items", out var items));
        Assert.AreEqual(JsonValueKind.Array, items.ValueKind);
        var call = queries.ReceivedCalls().Single(item => item.GetMethodInfo().Name == "QueryAsync");
        var parameters = (IReadOnlyDictionary<string, object?>)call.GetArguments()[1]!;
        Assert.AreEqual(actor.UserId, parameters["OwnerUserId"]);
        Assert.AreEqual(actor.TenantId, parameters["ScopeTenantId"]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.ExecuteAsync(invocation, actor with { TenantId = Guid.NewGuid() }, default));
        Assert.AreEqual(1, queries.ReceivedCalls().Count(item => item.GetMethodInfo().Name == "QueryAsync"));
    }
}
