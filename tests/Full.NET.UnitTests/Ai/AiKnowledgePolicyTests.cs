using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Serialization;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai;

/// <summary>分类及精确模型版本审批独立于网络连通性；默认拒绝全部文档处理。</summary>
[TestClass]
public sealed class AiKnowledgePolicyTests
{
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("Internal")]
    [DataRow("")]
    public void Unknown_classification_is_rejected(string classification) =>
        Assert.IsNotNull(AiKnowledgePolicy.Validate(classification, null, null, null, null));

    [TestMethod]
    [DataRow("public")]
    [DataRow("internal")]
    [DataRow("restricted")]
    public void Classified_but_unapproved_data_cannot_be_processed(string classification)
    {
        Assert.IsNull(AiKnowledgePolicy.Validate(classification, null, null, null, null));
        Assert.IsFalse(AiKnowledgePolicy.Allows(true, null, null, Guid.CreateVersion7(), 1));
    }

    [TestMethod]
    public void Approval_requires_both_identifier_and_positive_version()
    {
        var id = Guid.CreateVersion7();
        Assert.IsNotNull(AiKnowledgePolicy.Validate("internal", id, null, null, null));
        Assert.IsNotNull(AiKnowledgePolicy.Validate("internal", null, 1, null, null));
        Assert.IsNotNull(AiKnowledgePolicy.Validate("internal", id, 0, null, null));
        Assert.IsNotNull(AiKnowledgePolicy.Validate("internal", Guid.Empty, 1, null, null));
    }

    [TestMethod]
    public void Model_or_version_drift_and_disabled_base_reject_processing()
    {
        var id = Guid.CreateVersion7();
        Assert.IsTrue(AiKnowledgePolicy.Allows(true, id, 2, id, 2));
        Assert.IsFalse(AiKnowledgePolicy.Allows(true, id, 2, id, 3));
        Assert.IsFalse(AiKnowledgePolicy.Allows(true, id, 2, Guid.CreateVersion7(), 2));
        Assert.IsFalse(AiKnowledgePolicy.Allows(false, id, 2, id, 2));
    }

    [TestMethod]
    public void Creation_cannot_smuggle_scope_owner_or_model_approval()
    {
        var context = new AiJsonSerializerContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var field in new[] { "tenantId", "ownerUserId", "embeddingModelConfigId", "generationModelConfigId" })
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(
                $"{{\"name\":\"制度\",\"description\":null,\"{field}\":\"{Guid.CreateVersion7()}\"}}",
                context.CreateAiKnowledgeBaseRequest));
    }

    [TestMethod]
    public void Creation_accepts_the_documented_camel_case_contract()
    {
        var context = new AiJsonSerializerContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var request = JsonSerializer.Deserialize("{\"name\":\"制度\",\"description\":null}", context.CreateAiKnowledgeBaseRequest);
        Assert.AreEqual("制度", request!.Name);
    }
}
