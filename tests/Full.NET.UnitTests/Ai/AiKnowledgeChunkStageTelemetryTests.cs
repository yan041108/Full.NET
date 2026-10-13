using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>本地分块只观测一次完整边界；拒绝不泄漏正文、分类或模型绑定。</summary>
[TestClass][DoNotParallelize]
public sealed class AiKnowledgeChunkStageTelemetryTests
{
    [TestMethod]
    [DataRow("text", false, "success")][DataRow("text", false, "classification")][DataRow("text", false, "approval")][DataRow("text", false, "scope")][DataRow("text", false, "model")]
    [DataRow("text", true, "success")][DataRow("text", true, "classification")][DataRow("text", true, "approval")][DataRow("text", true, "scope")][DataRow("text", true, "model")]
    [DataRow("pdf", false, "success")][DataRow("pdf", false, "classification")][DataRow("pdf", false, "approval")][DataRow("pdf", false, "scope")][DataRow("pdf", false, "model")]
    [DataRow("pdf", true, "success")][DataRow("pdf", true, "classification")][DataRow("pdf", true, "approval")][DataRow("pdf", true, "scope")][DataRow("pdf", true, "model")]
    [DataRow("docx", false, "success")][DataRow("docx", false, "classification")][DataRow("docx", false, "approval")][DataRow("docx", false, "scope")][DataRow("docx", false, "model")]
    [DataRow("docx", true, "success")][DataRow("docx", true, "classification")][DataRow("docx", true, "approval")][DataRow("docx", true, "scope")][DataRow("docx", true, "model")]
    public void Stage_boundary_local_chunks_keep_original_scope_and_denial(string format, bool tenant, string fault)
    {
        using var capture = new AgentTelemetryCapture();
        Assert.AreEqual(fault == "success", Run(format, tenant, fault));
        StageTelemetryAssertions.Verify(capture, "knowledge_chunk_" + format, fault == "success" ? "completed" : "denied");
    }

    [TestMethod][DataRow("text")][DataRow("pdf")][DataRow("docx")]
    public void Stage_boundary_local_chunks_precancel_emits_nothing(string format)
    {
        using var capture = new AgentTelemetryCapture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => Run(format, false, "success", stop.Token));
        Assert.IsEmpty(capture.Spans); Assert.IsEmpty(capture.Measurements);
    }

    [TestMethod]
    [DataRow("text", "sample")][DataRow("text", "started")][DataRow("text", "stopped")][DataRow("text", "attempts")][DataRow("text", "duration")][DataRow("text", "none")]
    [DataRow("pdf", "sample")][DataRow("pdf", "started")][DataRow("pdf", "stopped")][DataRow("pdf", "attempts")][DataRow("pdf", "duration")][DataRow("pdf", "none")]
    [DataRow("docx", "sample")][DataRow("docx", "started")][DataRow("docx", "stopped")][DataRow("docx", "attempts")][DataRow("docx", "duration")][DataRow("docx", "none")]
    public void Stage_boundary_local_chunk_listener_fault_cannot_change_result(string format, string fault)
    {
        using var capture = StageTelemetryAssertions.FaultCapture(fault);
        Assert.IsTrue(Run(format, true, "success")); Assert.AreSame(capture.Root, System.Diagnostics.Activity.Current);
    }

    private static bool Run(string format, bool tenant, string fault, CancellationToken token = default)
    {
        Guid? scope = tenant ? Guid.CreateVersion7() : null;
        var knowledge = new Full.NET.Modules.Ai.Persistence.AiKnowledgeBaseRecord
        {
            Id = AiKnowledgeDocxPlanningTests.KnowledgeId, Version = 2, TenantId = scope, IsEnabled = true,
            DataClassification = fault == "classification" ? "secret-unknown" : "internal",
            EmbeddingModelConfigId = AiKnowledgeDocxPlanningTests.ModelId, EmbeddingModelVersion = fault == "approval" ? 4 : 3
        };
        var model = new Full.NET.Modules.Ai.Persistence.AiModelConfigRecord
        {
            Id = AiKnowledgeDocxPlanningTests.ModelId, Version = 3, TenantId = fault == "scope" ? Guid.CreateVersion7() : scope,
            IsEnabled = true, ProviderKey = "openai_compatible", EndpointBaseUrl = "https://api.openai.com/v1",
            ModelId = fault == "model" ? "secret-unapproved" : "text-embedding-3-small"
        };
        return format switch
        {
            "text" => AiKnowledgeModelChunker.Chunk(knowledge, model, new("secret-text", new string('a', 64), 97), AiKnowledgeTextFormat.PlainText, new(), token).IsSuccess,
            "pdf" => AiKnowledgePdfModelChunker.Chunk(knowledge, model, new(new string('a', 64), 97, [new(1, "secret-text"), new(2, "secret-second")]), new(), token).IsSuccess,
            "docx" => AiKnowledgeDocxModelChunker.Chunk(knowledge, model, AiKnowledgeDocxPlanningTests.Document("secret-text", "secret-second"), new(), token).IsSuccess,
            _ => throw new AssertFailedException("未知测试格式。")
        };
    }
}
