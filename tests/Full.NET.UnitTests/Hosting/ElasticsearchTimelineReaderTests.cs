using System.Net;
using System.Text;
using Full.NET.Abstractions.Observability;
using Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

/// <summary>传输边界的确定性回归；真实 ES 的筛选、证书和权限由集成测试验证。</summary>
[TestClass]
public sealed class ElasticsearchTimelineReaderTests
{
    [TestMethod]
    [DataRow("timed_out", "truncated")]
    [DataRow("failed_shard", "truncated")]
    [DataRow("oversize", "truncated")]
    [DataRow("missing_hits", "unavailable")]
    [DataRow("http_error", "unavailable")]
    public async Task Read_labels_partial_and_failed_responses_without_returning_body(string scenario, string status)
    {
        var body = scenario switch
        {
            "timed_out" => """{"timed_out":true,"hits":{"hits":[]}}""",
            "failed_shard" => """{"_shards":{"failed":1},"hits":{"hits":[]}}""",
            "oversize" => new string('x', 1024 * 1024 + 1),
            _ => "{}",
        };
        var factory = new Clients((_, _) => Task.FromResult(new HttpResponseMessage(
            scenario == "http_error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        var reader = Reader(factory);
        var result = await reader.ReadAsync(Query(), CancellationToken.None);
        Assert.AreEqual(status, result.Status);
        Assert.HasCount(0, result.Entries);
    }

    [TestMethod]
    public async Task Read_propagates_cancellation_to_transport()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Clients(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("取消后不能返回未完成响应。");
        });
        using var cancellation = new CancellationTokenSource();
        var pending = Reader(factory).ReadAsync(Query(), cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
    }

    private static ElasticsearchTimelineReader Reader(IHttpClientFactory clients) => new(clients,
        Options.Create(new RuntimeLogTimelineOptions
        {
            ElasticsearchEnabled = true,
            ElasticsearchEndpoint = "https://logs.example.test/",
        }));

    private static LogTimelineQuery Query() => new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow,
        new string('a', 32), null, null, null, 100);

    private sealed class Clients(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual(nameof(ElasticsearchTimelineReader), name);
            return new HttpClient(new Handler(send));
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
