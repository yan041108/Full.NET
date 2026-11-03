using System.Text.Json;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

namespace Full.NET.UnitTests.Ai;

/// <summary>元数据读取不能借用额外身份或错误 JSON 类型选择其他资源。</summary>
[TestClass]
public sealed class AiWorkflowMetadataArgumentTests
{
    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{\"sessionId\":17}")]
    [DataRow("{\"sessionId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [DataRow("{\"sessionId\":\"01900000-0000-7000-8000-000000000001\",\"tenantId\":\"01900000-0000-7000-8000-000000000002\"}")]
    [DataRow("{\"sessionId\":\"01900000-0000-7000-8000-000000000001\",\"sessionId\":\"01900000-0000-7000-8000-000000000002\"}")]
    public void Untrusted_metadata_argument_shapes_are_rejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(new GetChatSessionMetadataToolHandler(null!, null!).ValidateArguments(document.RootElement));
    }
}
