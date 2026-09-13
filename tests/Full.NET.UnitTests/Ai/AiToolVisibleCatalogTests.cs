using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>目录可见性按主体过滤，目录本身不能赋予执行权限。</summary>
[TestClass]
public sealed class AiToolVisibleCatalogTests
{
    [TestMethod]
    public async Task Only_authorized_and_bound_tools_are_visible_async()
    {
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync(AiAgentToolPermissions.CatalogRead, Arg.Any<CancellationToken>())
            .Returns(new ToolActor(Guid.NewGuid(), null, Guid.NewGuid()));
        var catalog = new AiAgentToolCatalogService(new FixedAgentToolRegistrySource(new AgentToolRegistry([
            new("ai.tools.ping", 1, AiAgentToolPermissions.CatalogRead, "none", true, new PingToolHandler()),
            new("ai.models.list", 1, AiModelPermissions.Read, "read", true, new PingToolHandler()),
            new("ai.chat.sessions.list", 1, AiChatPermissions.Read, "read", true, null)
        ])), authorization);
        var result = await catalog.ListAsync(default);
        CollectionAssert.AreEqual(new[] { "ai.tools.ping" }, result.Value!.Select(item => item.ToolName).ToArray());
        Assert.IsFalse((await catalog.GetByNameAsync("ai.models.list", default)).IsSuccess);
    }
}
