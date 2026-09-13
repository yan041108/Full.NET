using Full.NET.Agents.Mcp;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Mcp;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>MCP 暴露策略必须独立于远端 readOnlyHint，写工具默认不对外宣告。</summary>
[TestClass]
public sealed class McpExposurePolicyTests
{
    [TestMethod]
    [DataRow("write", McpExposurePolicy.ExposedKey, false)]
    [DataRow("read", McpExposurePolicy.InternalKey, false)]
    [DataRow("read", McpExposurePolicy.PlannedKey, false)]
    [DataRow("none", McpExposurePolicy.ExposedKey, true)]
    [DataRow("read", McpExposurePolicy.ExposedKey, true)]
    public void Catalog_item_exposure_respects_side_effect_and_key(string sideEffectKey, string exposureKey, bool expected)
    {
        var actual = McpExposurePolicy.IsCatalogItemExposable(exposureKey, sideEffectKey, true, true);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Rename_tool_remains_internal_and_write_side_effect()
    {
        var rename = AiAgentToolCatalog.Find("ai.chat.sessions.rename");
        Assert.IsNotNull(rename);
        Assert.AreEqual(McpExposurePolicy.InternalKey, rename.McpExposureKey);
        Assert.AreEqual(AiAgentToolSideEffectKeys.Write, rename.SideEffectKey);
        Assert.IsFalse(McpExposurePolicy.IsCatalogItemExposable(
            rename.McpExposureKey,
            rename.SideEffectKey,
            rename.IsEnabled,
            true));
    }

    [TestMethod]
    public void Read_tools_are_marked_for_mcp_exposure()
    {
        foreach (var toolName in new[] { "ai.tools.ping", "ai.models.list", "ai.chat.sessions.list" })
        {
            var tool = AiAgentToolCatalog.Find(toolName);
            Assert.IsNotNull(tool, toolName);
            Assert.AreEqual(McpExposurePolicy.ExposedKey, tool.McpExposureKey);
            Assert.IsTrue(McpExposurePolicy.IsCatalogItemExposable(
                tool.McpExposureKey,
                tool.SideEffectKey,
                tool.IsEnabled,
                true));
        }
    }

    [TestMethod]
    public async Task Exposure_catalog_hides_unauthorized_tools_async()
    {
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync(AiAgentToolPermissions.CatalogRead, Arg.Any<CancellationToken>())
            .Returns(new ToolActor(Guid.NewGuid(), null, Guid.NewGuid()));
        authorization.AuthorizeAsync(AiModelPermissions.Read, Arg.Any<CancellationToken>())
            .Returns((ToolActor?)null);
        authorization.AuthorizeAsync(AiChatPermissions.Read, Arg.Any<CancellationToken>())
            .Returns(new ToolActor(Guid.NewGuid(), null, Guid.NewGuid()));
        var registry = new AgentToolRegistry([
            new("ai.tools.ping", 1, AiAgentToolPermissions.CatalogRead, "none", true, Substitute.For<IAgentToolHandler>()),
            new("ai.models.list", 1, AiModelPermissions.Read, "read", true, Substitute.For<IAgentToolHandler>()),
            new("ai.chat.sessions.list", 1, AiChatPermissions.Read, "read", true, Substitute.For<IAgentToolHandler>()),
        ]);
        var catalog = new AiMcpToolExposureCatalog(new FixedAgentToolRegistrySource(registry), authorization);
        var tools = await catalog.ListAuthorizedAsync();
        CollectionAssert.AreEquivalent(
            new[] { "ai.tools.ping", "ai.chat.sessions.list" },
            tools.Select(item => item.ToolName).ToArray());
    }
}
