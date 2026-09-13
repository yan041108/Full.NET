using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiAgentToolCatalogTests
{
    [TestMethod]
    public void Catalog_only_exposes_approved_side_effect_categories()
    {
        foreach (var tool in AiAgentToolCatalog.List())
        {
            var allowed = tool.SideEffectKey is AiAgentToolSideEffectKeys.None or AiAgentToolSideEffectKeys.Read
                || (tool.SideEffectKey == AiAgentToolSideEffectKeys.Write && tool.ToolName == "ai.chat.sessions.rename");
            Assert.IsTrue(allowed, tool.ToolName);
        }
    }

    [TestMethod]
    public void Catalog_entries_have_unique_tool_names()
    {
        var names = AiAgentToolCatalog.List().Select(item => item.ToolName).ToArray();
        CollectionAssert.AllItemsAreUnique(names);
    }
}
