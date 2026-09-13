using Full.NET.Agents.Runtime;
using Microsoft.Extensions.AI;
using System.Text.Json;
using NSubstitute;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>使用真实 SDK 完整会话快照，脚本化中立客户端不访问外部模型。</summary>
[TestClass]
public sealed class AiAgentFrameworkTests
{
    [TestMethod]
    public async Task Cancelled_execution_does_not_dispatch_or_dispose_the_borrowed_client()
    {
        var client = Substitute.For<IChatClient>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AgentFrameworkRuntime.CreateRunner().RunAsync(client, "hello", cancellation.Token));
        await client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        client.DidNotReceive().Dispose();
    }

    [TestMethod]
    public void Diagnostic_result_does_not_expose_model_text_or_session()
    {
        using var document = JsonDocument.Parse("{\"private\":\"session-secret\"}");
        var result = new AgentModelResult("model-secret", document.RootElement, null, null);
        Assert.DoesNotContain("model-secret", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("session-secret", result.ToString(), StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow("openai_compatible", "data: {\"choices\":[{\"delta\":{\"content\":\"reply\"}}]}\n\ndata: [DONE]\n\n")]
    [DataRow("ollama", "{\"message\":{\"content\":\"reply\"},\"done\":true,\"prompt_eval_count\":2,\"eval_count\":1}\n")]
    public async Task Adapter_runs_with_real_provider_and_restores_without_network_dispatch(string provider, string body)
    {
        var handler = new ScriptHandler(body);
        using var http = new HttpClient(handler);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(http);
        using var scope = new AiModelBindingScope();
        var factory = TestAiProviders.Create(httpFactory, scope).Single(item => item.ProviderKey == provider);
        using var client = await factory.CreateChatClientAsync(scope.Create(new AiModelConfigRecord
        {
            Id = Guid.CreateVersion7(), Version = 1, ProviderKey = provider, ModelId = "model",
            EndpointBaseUrl = "https://provider.test", ApiKeyProtected = TestAiProviders.Protect("test-key")
        }), CancellationToken.None);
        var result = await AgentFrameworkRuntime.CreateRunner().RunAsync(client, "hello", CancellationToken.None);
        Assert.AreEqual("reply", result.Text);
        await AgentFrameworkRuntime.CreateRunner().ValidateSessionAsync(client, result.Session, CancellationToken.None);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task Adapter_serializes_session_and_restores_without_another_model_call()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => new ChatResponse(new ChatMessage(ChatRole.Assistant, "reply")) { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } });
        var adapter = AgentFrameworkRuntime.CreateRunner();
        var result = await adapter.RunAsync(client, "hello", CancellationToken.None);
        Assert.AreEqual("reply", result.Text);
        Assert.AreEqual(JsonValueKind.Object, result.Session.ValueKind);
        Assert.Contains("hello", result.Session.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("reply", result.Session.GetRawText(), StringComparison.Ordinal);
        Assert.AreEqual(2L, result.InputTokens);
        Assert.AreEqual(1L, result.OutputTokens);
        Assert.IsNull(client.ReceivedCalls().Single(call => call.GetMethodInfo().Name == "GetResponseAsync").GetArguments()[1],
            "单次文本适配器不得向仅支持固定配置的 Provider 传入选项。");
        await adapter.ValidateSessionAsync(client, result.Session, CancellationToken.None);
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        client.DidNotReceive().Dispose();
    }

    private sealed class ScriptHandler(string body) : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
