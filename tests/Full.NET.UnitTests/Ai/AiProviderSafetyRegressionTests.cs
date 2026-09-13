using System.Net;
using System.Text;
using Full.NET.Modules.Ai.Connectivity;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>用受控响应验证不可信供应商数据的完整性、读取上限和失败边界。</summary>
[TestClass]
public sealed class AiProviderSafetyRegressionTests
{
    [TestMethod]
    public async Task Ollama_repeated_chunks_are_preserved()
    {
        const string body = "{\"message\":{\"content\":\"哈\"},\"done\":false}\n"
            + "{\"message\":{\"content\":\"哈\"},\"done\":false}\n"
            + "{\"message\":{\"content\":\"哈哈\"},\"done\":false}\n"
            + "{\"done\":true,\"prompt_eval_count\":1,\"eval_count\":4}\n";
        using var client = Client(body);
        var result = await TestAiProviders.Streamer(Factory(client)).StreamAsync(
            Model("ollama"), [], _ => Task.CompletedTask);
        Assert.AreEqual("哈哈哈哈", result.Content);
    }

    [TestMethod]
    [DataRow("ollama", "{\"message\":{\"content\":\"partial\"},\"done\":false}\n")]
    [DataRow("openai_compatible", "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n")]
    public async Task Unexpected_eof_is_not_a_completed_generation(string provider, string body)
    {
        using var client = Client(body);
        var partial = new StringBuilder();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            TestAiProviders.Streamer(Factory(client)).StreamAsync(
                Model(provider), [], _ => Task.CompletedTask, contentBuffer: partial));
        Assert.AreEqual("partial", partial.ToString());
    }

    [TestMethod]
    [DataRow("ollama", "{\"error\":\"private-provider-message\"}\n")]
    [DataRow("openai_compatible", "data: {\"error\":{\"message\":\"private-provider-message\"}}\n\ndata: [DONE]\n")]
    public async Task Error_frames_fail_without_echoing_provider_text(string provider, string body)
    {
        using var client = Client(body);
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            TestAiProviders.Streamer(Factory(client)).StreamAsync(
                Model(provider), [], _ => Task.CompletedTask));
        Assert.DoesNotContain("private-provider-message", error.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void External_error_never_echoes_arbitrary_content()
    {
        Assert.DoesNotContain("private-value", AiChatContentPolicy.SanitizeExternalError(
            "upstream password=private-value"), StringComparison.Ordinal);
    }

    [TestMethod]
    public void Arbitrary_json_is_not_a_safe_audit_summary()
    {
        Assert.DoesNotContain("private-value", AiAgentToolAuditPolicy.Summarize(
            "{\"nested\":{\"password\":\"private-value\"}}"), StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Connectivity_errors_do_not_echo_response_body()
    {
        using var client = Client("private-provider-message", HttpStatusCode.BadRequest);
        var result = await TestAiProviders.Tester(Factory(client)).TestAsync(Model("ollama"));
        Assert.IsFalse(result.Succeeded);
        Assert.DoesNotContain("private-provider-message", result.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Connectivity_cancellation_remains_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = Client("{}");
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            TestAiProviders.Tester(Factory(client)).TestAsync(Model("ollama"), cancellation.Token));
    }

    [TestMethod]
    public async Task Connectivity_stops_reading_an_oversized_response()
    {
        using var stream = new CountingStream(Encoding.UTF8.GetBytes(new string('x', 1024 * 1024)));
        using var client = new HttpClient(new Handler(new StreamContent(stream)));
        var result = await TestAiProviders.Tester(Factory(client)).TestAsync(Model("ollama"));
        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(stream.BytesRead <= 65537, $"读取了 {stream.BytesRead} 字节，超过响应探测预算。");
    }

    private static AiModelConfigRecord Model(string provider) => new()
    {
        IsEnabled = true, ApiKeyProtected = TestAiProviders.Protect("test-key"), ProviderKey = provider, EndpointBaseUrl = "https://provider.test", ModelId = "test",
    };

    private static HttpClient Client(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new Handler(new StringContent(body), status));

    private static IHttpClientFactory Factory(HttpClient client)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        return factory;
    }

    private sealed class Handler(HttpContent content, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    /// <summary>避免 MemoryStream.CopyToAsync 绕开计数，以实际读入字节证明预算。</summary>
    private sealed class CountingStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
