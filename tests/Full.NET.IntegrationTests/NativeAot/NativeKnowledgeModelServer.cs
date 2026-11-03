using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只在临时容器的内部网络与临时 CA 中承载受控模型；正式宿主保留 DNS、证书和目的地校验。</summary>
internal sealed class NativeKnowledgeModelServer(WebApplication application) : IAsyncDisposable
{
    internal ConcurrentQueue<string[]> EmbeddingInputs { get; } = new();
    internal ConcurrentQueue<string[]> StreamingInputs { get; } = new();
    internal int ChatCalls;
    internal bool RejectEmbeddings;
    internal bool OmitEmbeddingUsage { get; set; }
    internal bool OmitChatUsage { get; set; }
    internal string? RefinementFault { get; set; }
    internal bool PreferPersonalScheduleCitation { get; set; }

    internal static async Task<NativeKnowledgeModelServer> StartAsync()
    {
        var address = Environment.GetEnvironmentVariable("FULLNET_NATIVE_MODEL_ADDRESS");
        var certificate = Environment.GetEnvironmentVariable("FULLNET_NATIVE_MODEL_CERTIFICATE");
        Assert.IsTrue(IPAddress.TryParse(address, out var ip), "必须在受控容器内显式提供模型网络地址。");
        Assert.IsTrue(!IPAddress.IsLoopback(ip!) && File.Exists(certificate), "缺少临时 TLS 证书。");
        var resolved = await Dns.GetHostAddressesAsync("api.openai.com");
        Assert.IsTrue(resolved.Length == 1 && resolved[0].Equals(ip), "拒绝连接真实云地址；验收 DNS 必须精确指向本容器。");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(ip!, 443,
            listen => listen.UseHttps(X509CertificateLoader.LoadPkcs12FromFile(certificate!, "fullnet-test-only"))));
        var application = builder.Build();
        var server = new NativeKnowledgeModelServer(application);
        application.MapPost("/v1/embeddings", async (HttpContext context) =>
        {
            Assert.AreEqual("Bearer native-test-placeholder", context.Request.Headers.Authorization.ToString());
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var input = body.RootElement.GetProperty("input");
            var inputs = input.ValueKind == JsonValueKind.Array ? input.EnumerateArray().Select(x => x.GetString()!).ToArray() : [input.GetString()!];
            server.EmbeddingInputs.Enqueue(inputs);
            if (server.RejectEmbeddings) { context.Response.StatusCode = 503; return; }
            Assert.AreEqual("text-embedding-3-small", body.RootElement.GetProperty("model").GetString());
            var vector = new float[1536]; vector[0] = 1;
            await context.Response.WriteAsJsonAsync(new { data = inputs.Select((_, index) => new { index, embedding = vector }),
                usage = server.OmitEmbeddingUsage ? null : new { prompt_tokens = inputs.Length, total_tokens = inputs.Length } }, context.RequestAborted);
        });
        application.MapPost("/v1/chat/completions", async (HttpContext context) =>
        {
            Assert.AreEqual("Bearer native-test-placeholder", context.Request.Headers.Authorization.ToString());
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var user = body.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
            using var knowledge = ReadKnowledge(user);
            // 缓冲问答的正式适配器同样使用SSE；按明确知识数据包区分用途，不能只看stream标志。
            if (knowledge is null && body.RootElement.TryGetProperty("stream", out var streaming) && streaming.ValueKind == JsonValueKind.True)
            {
                Interlocked.Increment(ref server.ChatCalls);
                Assert.IsFalse(body.RootElement.TryGetProperty("tools", out _));
                server.StreamingInputs.Enqueue(body.RootElement.GetProperty("messages").EnumerateArray().Select(value => value.GetProperty("content").GetString()!).ToArray());
                context.Response.ContentType = "text/event-stream";
                var streamFrame = JsonSerializer.Serialize(new { id = "controlled-stream", @object = "chat.completion.chunk", created = 0, model = "gpt-4.1-mini",
                    choices = new[] { new { index = 0, delta = new { role = "assistant", content = "当前来源已核证的普通续谈" }, finish_reason = (string?)null } } });
                var streamUsage = JsonSerializer.Serialize(new { id = "controlled-stream", @object = "chat.completion.chunk", created = 0, model = "gpt-4.1-mini",
                    choices = Array.Empty<object>(), usage = new { prompt_tokens = 5, completion_tokens = 3, total_tokens = 8 } });
                await context.Response.WriteAsync("data: " + streamFrame + "\n\ndata: " + streamUsage + "\n\ndata: [DONE]\n\n", context.RequestAborted);
                return;
            }
            Assert.IsNotNull(knowledge);
            Interlocked.Increment(ref server.ChatCalls);
            if (server.RefinementFault == "reject") { context.Response.StatusCode = 503; return; }
            Assert.IsFalse(body.RootElement.TryGetProperty("tools", out _));
            var answer = server.RefinementFault=="answer_refusal"?JsonSerializer.Serialize(new{answer="",citationIds=Array.Empty<Guid>(),refusal=true}):knowledge.RootElement.TryGetProperty("task", out _) ? KnowledgeRefinementReply.Create(knowledge.RootElement, server.RefinementFault)
                : JsonSerializer.Serialize(new { answer = "原生完整生成验证", citationIds = new[] { (server.PreferPersonalScheduleCitation
                    ? knowledge.RootElement.GetProperty("sources").EnumerateArray().First(source => source.TryGetProperty("sourceKind", out var kind) && kind.GetString() == "calendar-personal-schedule-v1")
                    : knowledge.RootElement.GetProperty("sources")[0]).GetProperty("citationId").GetGuid() }, refusal = false });
            var frame = JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = answer } } }, usage = server.OmitChatUsage ? null : new { prompt_tokens = 7, completion_tokens = 3 } });
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync($"data: {frame}\n\ndata: [DONE]\n\n", context.RequestAborted);
        });
        await application.StartAsync();
        return server;
    }

    private static JsonDocument? ReadKnowledge(string value)
    {
        JsonDocument? parsed = null;
        try
        {
            parsed = JsonDocument.Parse(value);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object
                && (parsed.RootElement.TryGetProperty("sources", out _) || parsed.RootElement.TryGetProperty("task", out _))) return parsed;
        }
        catch (JsonException) { }
        parsed?.Dispose(); return null;
    }

    public async ValueTask DisposeAsync() { await application.StopAsync(); await application.DisposeAsync(); }
}
