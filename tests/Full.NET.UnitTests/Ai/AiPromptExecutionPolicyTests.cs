using Full.NET.Agents.Definitions;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>旧正文与摘要逐字兼容，新策略完整绑定静态目录，不允许降级或工具扩张。</summary>
[TestClass]
public sealed class AiPromptExecutionPolicyTests
{
    private static AiPromptBody Body(string key) => new(Guid.CreateVersion7(), "zh-CN", "{{input}}", 100, 100)
    { Execution = AiPromptExecutionPolicy.Resolve(key) };

    [TestMethod]
    public void Legacy_serialization_and_hash_remain_byte_compatible()
    {
        var body = new AiPromptBody(Guid.Parse("01900000-0000-7000-8000-000000000001"), "zh-CN", "{{input}}", 100, 100);
        var json = AiPromptVersionIntegrity.Serialize(body);
        // 固定历史字节和摘要，避免新旧两条路径同时漂移仍互相证明通过。
        const string expectedJson = """{"ModelConfigId":"01900000-0000-7000-8000-000000000001","LanguageTag":"zh-CN","Template":"{{input}}","InputTokenLimit":100,"OutputTokenLimit":100}""";
        const string expectedHash = "e5ec551379bb52c9e3a4272827b949af7dc8eb02c406f408771a4eb46e1bbef6";
        Assert.AreEqual(expectedJson, json);
        Assert.AreEqual(expectedHash, AiPromptVersionIntegrity.Hash(json, 1, "ollama", "model"));
        Assert.AreEqual(expectedHash, AiPromptVersionIntegrity.Hash(body, json, 1, "ollama", "model"));
    }
    [TestMethod]
    [DataRow(AgentDefinitionRegistry.SingleTextKey)][DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey)][DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey)]
    public void Current_static_definition_roundtrips_with_distinct_hash_domain(string key)
    {
        var body = Body(key); Assert.IsTrue(AiPromptPolicy.IsValid(body));
        var json = AiPromptVersionIntegrity.Serialize(body); var hash = AiPromptVersionIntegrity.Hash(body, json, 1, "ollama", "model");
        var row = new AiPromptVersionRecord { Id = Guid.CreateVersion7(), PromptId = Guid.CreateVersion7(), Version = 1, FormatVersion = 1,
            ModelConfigVersion = 1, ProviderKey = "ollama", ModelId = "model", BodyJson = json, BodyHash = hash };
        Assert.IsNotNull(AiPromptVersionIntegrity.Decode(row));
        Assert.AreNotEqual(hash, AiPromptVersionIntegrity.Hash(json, 1, "ollama", "model"));
        Assert.IsNull(AiPromptVersionIntegrity.Decode(new AiPromptVersionRecord { Id = row.Id, PromptId = row.PromptId, Version = row.Version,
            FormatVersion = row.FormatVersion, ModelConfigVersion = row.ModelConfigVersion, ProviderKey = row.ProviderKey, ModelId = row.ModelId,
            BodyHash = row.BodyHash, BodyJson = AiPromptVersionIntegrity.Serialize(body with { Execution = null }) }));
    }
    [TestMethod]
    [DataRow("version")][DataRow("definition")][DataRow("digest")][DataRow("input")][DataRow("output")][DataRow("tools")][DataRow("retrieval")]
    public void Static_strategy_drift_is_rejected(string field)
    {
        var body = Body(AgentDefinitionRegistry.ReadOnlyToolLoopKey); var binding = body.Execution!;
        binding = field switch
        {
            "version" => binding with { PolicyVersion = 3 }, "definition" => binding with { DefinitionVersion = 2 },
            "digest" => binding with { DefinitionDigest = new('a', 64) }, "input" => binding with { InputSchemaKey = "custom" },
            "output" => binding with { OutputSchemaKey = "custom" }, "tools" => binding with { AllowedToolNames = ["ai.chat.sessions.rename"] },
            _ => binding with { RetrievalStrategyKey = "fusion-rerank" }
        };
        Assert.IsFalse(AiPromptPolicy.IsValid(body with { Execution = binding }));
    }
    [TestMethod]
    public void Workflow_cannot_replace_registered_node_templates()
    {
        Assert.IsFalse(AiPromptPolicy.IsValid(Body(AgentWorkflowRegistry.ChatRenameWorkflowKey) with { Template = "Override {{input}}" }));
        Assert.IsNull(AiPromptExecutionPolicy.Resolve("unregistered"));
    }
}
