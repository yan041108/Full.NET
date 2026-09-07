using System.Net;
using System.Text;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>通过真实流协议解析验证提供程序响应的内存边界。</summary>
[TestClass]
public sealed class AiChatStreamingBudgetTests
{
    /// <summary>一个巨大协议帧不得进入 JSON 解析或下游发送。</summary>
    [TestMethod]
    public async Task Oversized_frame_is_rejected_before_delta_async()
    {
        using var client = new HttpClient(new ResponseHandler("data: " + new string('x', 70000) + "\n"));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var count = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new AiChatCompletionStreamer(factory).StreamAsync(
            Model(), "test-key", [], _ => { count++; return Task.CompletedTask; }));
        Assert.AreEqual(0, count);
    }

    /// <summary>多个合法小帧的累计正文也必须停止增长。</summary>
    [TestMethod]
    public async Task Cumulative_content_has_a_hard_limit_async()
    {
        var frame = "data: {\"choices\":[{\"delta\":{\"content\":\"" + new string('x', 8192) + "\"}}]}\n";
        using var client = new HttpClient(new ResponseHandler(string.Concat(Enumerable.Repeat(frame, 200))));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var characters = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new AiChatCompletionStreamer(factory).StreamAsync(
            Model(), "test-key", [], delta => { characters += delta.Length; return Task.CompletedTask; }));
        Assert.IsTrue(characters <= 1024 * 1024);
    }

    /// <summary>SSE 的 CR、LF 与 CRLF 换行都必须保留即时增量语义。</summary>
    /// <param name="newline">协议换行序列。</param>
    [TestMethod]
    [DataRow("\r")]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public async Task Valid_line_endings_preserve_content_async(string newline)
    {
        var body = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}" + newline + newline + "data: [DONE]" + newline;
        using var client = new HttpClient(new ResponseHandler(body));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var result = await new AiChatCompletionStreamer(factory).StreamAsync(Model(), "test-key", [], _ => Task.CompletedTask);
        Assert.AreEqual("hello", result.Content);
    }

    /// <summary>跨固定缓冲区的 CRLF 只算一个换行，不能凭空生成空行。</summary>
    [TestMethod]
    public async Task CrLf_crossing_buffer_boundary_is_one_newline_async()
    {
        using var reader = new StringReader(new string('x', 4095) + "\r\nnext\rfinal");
        var lines = new AiResponseLineReader(reader);
        Assert.AreEqual(new string('x', 4095), await lines.ReadLineAsync(default));
        Assert.AreEqual("next", await lines.ReadLineAsync(default));
        Assert.AreEqual("final", await lines.ReadLineAsync(default));
        Assert.IsNull(await lines.ReadLineAsync(default));
    }

    /// <summary>普通流片段的 usage:null 与末尾用量帧必须共同支持。</summary>
    [TestMethod]
    public async Task Null_usage_chunks_do_not_abort_generation_async()
    {
        const string body = "data: {\"usage\":null,\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n"
            + "data: {\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2},\"choices\":[]}\n\ndata: [DONE]\n";
        using var client = new HttpClient(new ResponseHandler(body));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var result = await new AiChatCompletionStreamer(factory).StreamAsync(Model(), "test-key", [], _ => Task.CompletedTask);
        Assert.AreEqual("hello", result.Content);
        Assert.AreEqual(10, result.PromptTokens);
        Assert.AreEqual(2, result.CompletionTokens);
    }

    /// <summary>构建仅用于隔离协议读取测试的模型。</summary>
    private static AiModelConfigRecord Model() => new()
    {
        ProviderKey = "openai_compatible", EndpointBaseUrl = "https://provider.test", ModelId = "test",
    };

    /// <summary>提供固定协议内容，测试不连接外部服务。</summary>
    /// <param name="body">流式协议响应。</param>
    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        /// <summary>返回测试响应流。</summary>
        /// <param name="request">待发送请求。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) });
    }
}
