using Full.NET.AgenticWeb.Mcp.Client;
using Full.NET.Agents.Mcp;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class McpRemoteCapabilityPolicyTests
{
    [TestMethod]
    public void Build_local_tool_name_is_namespaced()
    {
        var name = McpRemoteCapabilityPolicy.BuildLocalToolName("loopback", "ai.tools.ping");
        Assert.AreEqual("remote.loopback.ai.tools.ping", name);
    }

    [TestMethod]
    public void Schema_hash_is_stable_for_equivalent_json()
    {
        const string a = """{"type":"object","additionalProperties":false}""";
        const string b = """
            {
              "type": "object",
              "additionalProperties": false
            }
            """;
        Assert.AreEqual(McpRemoteCapabilityPolicy.ComputeSchemaHash(a), McpRemoteCapabilityPolicy.ComputeSchemaHash(b));
    }

    [TestMethod]
    public void Write_side_effect_cannot_be_approved()
    {
        Assert.IsFalse(McpRemoteCapabilityPolicy.CanApproveForExposure("write", "{}", out var rejection));
        Assert.AreEqual("ai.mcp.remote_write_forbidden", rejection);
    }

    [TestMethod]
    public void Drifted_schema_blocks_execution()
    {
        var tool = new McpRemoteApprovedTool(
            Guid.CreateVersion7(),
            "loopback",
            new Uri("http://127.0.0.1:5000/mcp"),
            "remote.loopback.ai.tools.ping",
            "ai.tools.ping",
            1,
            """{"type":"object","additionalProperties":false}""",
            McpRemoteCapabilityPolicy.ComputeSchemaHash("""{"type":"object","additionalProperties":false}"""),
            "none",
            AiMcpPermissions.RemoteInvoke,
            McpRemoteCapabilityPolicy.ApprovedStatusKey,
            "service-token");
        Assert.IsFalse(McpRemoteCapabilityPolicy.CanExecute(
            tool,
            McpRemoteCapabilityPolicy.ComputeSchemaHash("""{"type":"object","properties":{"x":{"type":"string"}}}"""),
            out var rejection));
        Assert.AreEqual("ai.mcp.remote_schema_drift", rejection);
    }
}
