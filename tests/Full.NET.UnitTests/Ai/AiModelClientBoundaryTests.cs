using Full.NET.Modules.Ai.Streaming;

namespace Full.NET.UnitTests.Ai;

/// <summary>聊天编排不应拥有供应商 HTTP 传输，以免新增模型再次侵入业务模块。</summary>
[TestClass]
public sealed class AiModelClientBoundaryTests
{
    [TestMethod]
    public void Chat_orchestrator_does_not_depend_on_http_client_factory()
    {
        var dependencies = typeof(AiChatCompletionStreamer).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType);
        Assert.IsFalse(dependencies.Contains(typeof(IHttpClientFactory)));
    }
}
