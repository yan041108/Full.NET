using System.Net;
using System.Text;
using Full.NET.LogConsumer;

namespace Full.NET.UnitTests.Logging;

[TestClass]
public sealed class ElasticsearchLogDocumentSinkTests
{
    private const string EventId = "0199aa18-3e3b-7000-8000-5a8ab7a1f404";

    [TestMethod]
    public async Task BatchMixedResultsStayInRequestOrderWithOneHttpRequest()
    {
        var ids = new[] { EventId, "0199aa18-3e3b-7000-8000-5a8ab7a1f405", "0199aa18-3e3b-7000-8000-5a8ab7a1f406" };
        var calls = 0;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            calls++;
            var lines = (await request.Content!.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.HasCount(6, lines);
            for (var index = 0; index < ids.Length; index++)
            {
                StringAssert.Contains(lines[index * 2], ids[index]);
                StringAssert.Contains(lines[index * 2 + 1], ids[index]);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"errors\":true,\"items\":[" + string.Join(',', ids.Select((id, index) =>
                    "{\"index\":{\"_index\":\"fn-logs-2-diagnostic-2026.09.30\",\"_id\":\"" + id
                    + "\",\"status\":" + new[] { 201, 429, 400 }[index] + "}}")) + "]}") };
        }));
        var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"));
        var results = await sink.WriteBatchAsync(ids.Select(id => new LogDocumentWrite(Record(id), "fn-logs-2-diagnostic-2026.09.30")).ToArray(), default);
        CollectionAssert.AreEqual(new[] { BulkItemOutcome.Succeeded, BulkItemOutcome.Retry, BulkItemOutcome.Isolate }, results);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task BatchBudgetsRejectBeforeSending()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ => { calls++; throw new InvalidOperationException(); }));
        var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"));
        var write = new LogDocumentWrite(Record(), "fn-logs-2-diagnostic-2026.09.30");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sink.WriteBatchAsync([], default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sink.WriteBatchAsync(Enumerable.Repeat(write, 65).ToArray(), default));
        var large = new LogDocumentWrite(Record(EventId, 65000), write.IndexName);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sink.WriteBatchAsync(Enumerable.Repeat(large, 17).ToArray(), default));
        var nearLimit = new LogDocumentWrite(Record(EventId, 65535 - (Record().Utf8Json.Length - 1)), write.IndexName);
        Assert.IsTrue((nearLimit.Record.Utf8Json.Length - 1) * 16 <= 1024 * 1024);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sink.WriteBatchAsync(Enumerable.Repeat(nearLimit, 16).ToArray(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => sink.WriteBatchAsync([write, new LogDocumentWrite(Record(), "other")], default));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task BatchUnprovableOrOversizedResponseRetriesEveryItem()
    {
        foreach (var body in new[]
        {
            "{\"errors\":false,\"items\":[]}",
            "{\"errors\":false,\"items\":[{\"index\":{\"status\":201,\"_index\":\"other\",\"_id\":\"wrong\"}},{\"index\":{\"status\":201}}]}",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                errors = false,
                padding = new string('x', 65537),
                items = Enumerable.Repeat(new { index = new { status = 201,
                    _index = "fn-logs-2-diagnostic-2026.09.30", _id = EventId } }, 2).ToArray(),
            }),
        })
        {
            using var http = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body) })));
            var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"));
            var write = new LogDocumentWrite(Record(), "fn-logs-2-diagnostic-2026.09.30");
            CollectionAssert.AreEqual(new[] { BulkItemOutcome.Retry, BulkItemOutcome.Retry },
                await sink.WriteBatchAsync([write, write], default));
        }
    }

    [TestMethod]
    public async Task BatchCancellationAndDeadlineDoNotBecomeConfirmation()
    {
        using var http = new HttpClient(new HangingHandler());
        var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"), TimeSpan.FromMilliseconds(20));
        var write = new LogDocumentWrite(Record(), "fn-logs-2-diagnostic-2026.09.30");
        await Assert.ThrowsAsync<OperationCanceledException>(() => sink.WriteBatchAsync([write, write], default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => sink.WriteBatchAsync([write, write], cancelled.Token));
    }

    [TestMethod]
    public async Task BatchDeadlineIncludesHangingResponseBodyAfterHeaders()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new HangingResponseStream()) })));
        var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"), TimeSpan.FromMilliseconds(20));
        var write = new LogDocumentWrite(Record(), "fn-logs-2-diagnostic-2026.09.30");
        await Assert.ThrowsAsync<OperationCanceledException>(() => sink.WriteBatchAsync([write, write], default));
    }

    [TestMethod]
    public async Task ValidRecordUsesFixedIndexAndIdAndRequiresItemConfirmation()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://es.example:9200/_bulk", request.RequestUri!.ToString());
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"errors\":false,\"items\":[{\"index\":{\"_index\":\"fn-logs-2-diagnostic-2026.09.30\",\"_id\":\"" + EventId + "\",\"status\":201}}]}")
            };
        });
        using var http = new HttpClient(handler);
        var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"));

        Assert.AreEqual(BulkItemOutcome.Succeeded,
            await sink.WriteAsync(Record(), "fn-logs-2-diagnostic-2026.09.30", default));
        StringAssert.StartsWith(requestBody!, "{\"index\":{\"_index\":\"fn-logs-2-diagnostic-2026.09.30\",\"_id\":\"" + EventId + "\"}}\n");
        StringAssert.Contains(requestBody!, "\"LogEventId\":\"" + EventId + "\"");
        Assert.IsTrue(requestBody!.EndsWith('\n'));
        Assert.IsFalse(requestBody.EndsWith("\n\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task IncompleteBulkResponseMustRetry()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"errors\":false,\"items\":[]}") })));
        var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"));
        Assert.AreEqual(BulkItemOutcome.Retry,
            await sink.WriteAsync(Record(), "fn-logs-2-diagnostic-2026.09.30", default));
    }

    [TestMethod]
    public async Task SuccessfulStatusWithoutMatchingTargetMustRetainOffset()
    {
        var responses = new[]
        {
            """
                {"errors":false,"items":[{"index":{"_index":"fn-logs-2-diagnostic-2026.09.30","_id":"0199aa18-3e3b-7000-8000-000000000000","status":201}}]}
                """,
            """
                {"errors":false,"items":[{"index":{"_index":"fn-logs-2-diagnostic-2026.09.29","_id":"0199aa18-3e3b-7000-8000-5a8ab7a1f404","status":201}}]}
                """,
            """
                {"errors":false,"items":[{"index":{"_index":"fn-logs-2-diagnostic-2026.09.30","status":201}}]}
                """,
        };
        foreach (var response in responses)
        {
            using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(response),
                })));
            var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"));

            Assert.AreEqual(BulkItemOutcome.Retry,
                await sink.WriteAsync(Record(), "fn-logs-2-diagnostic-2026.09.30", default));
        }
    }

    [TestMethod]
    public async Task RedirectResponseMustNotConfirmDocument()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://other.example/_bulk") },
            })));
        var sink = new ElasticsearchLogDocumentSink(http, new Uri("https://es.example:9200/"));
        Assert.AreEqual(BulkItemOutcome.Retry,
            await sink.WriteAsync(Record(), "fn-logs-2-diagnostic-2026.09.30", default));
    }

    [TestMethod]
    public async Task WholeBulkOperationHasBoundedDeadline()
    {
        using var http = new HttpClient(new HangingHandler());
        var sink = new ElasticsearchLogDocumentSink(http,
            new Uri("https://es.example:9200/"), TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sink.WriteAsync(Record(), "fn-logs-2-diagnostic-2026.09.30", default));
    }

    private static ParsedLogRecord Record(string id = EventId, int padding = 0)
    {
        var json = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"ok","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-30T00:00:00Z","ExpiresAtUtc":"2026-10-30T00:00:00Z","IndexRouteVersion":2,"Padding":"{{new string('x', padding)}}"}
            """ + "\n");
        Assert.AreEqual(LogRecordValidationResult.Valid,
            KafkaLogRecordParser.TryParse(id, json, 65536, out var record));
        return record!;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class HangingResponseStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
