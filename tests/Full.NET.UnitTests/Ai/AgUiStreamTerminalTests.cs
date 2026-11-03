using System.Text;
using System.Text.Json;
using Full.NET.Agents.AgUi;
using Full.NET.AgenticWeb.AgUi;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AG-UI：Stream Terminal。</summary>
[TestClass]
public sealed class AgUiStreamTerminalTests
{
    [TestMethod]
    [DataRow("cancelled")] [DataRow("completed")] [DataRow("failed")]
    public async Task Terminal_run_drains_all_event_pages_before_closing(string status)
    {
        var reader = new Reader(status, 101);
        for (var i = 1; i <= 100; i++) reader.Events.Add(new(i, "run.node_started", 1, "{}", DateTimeOffset.UtcNow));
        reader.Events.Add(new(101, "run." + status, 1, "{}", DateTimeOffset.UtcNow));
        var body = await StreamAsync(reader, "terminal-" + status);
        Assert.AreEqual(2, reader.EventReads);
        Assert.AreEqual(101, Count(body, "event: CUSTOM\n"));
        Assert.AreEqual(1, Count(body, status == "completed" ? "event: RUN_FINISHED\n" : "event: RUN_ERROR\n"));
        Assert.IsTrue(body.LastIndexOf("event: STATE_SNAPSHOT", StringComparison.Ordinal) > body.LastIndexOf("event: CUSTOM", StringComparison.Ordinal));
        var end = status == "completed" ? "event: RUN_FINISHED" : "event: RUN_ERROR";
        Assert.IsTrue(body.LastIndexOf(end, StringComparison.Ordinal) > body.LastIndexOf("event: STATE_SNAPSHOT", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Cancellation_between_snapshot_and_events_emits_boundary_once_and_refreshes_state()
    {
        var reader = new Reader("running", 0);
        reader.OnEvents = () => { reader.Status = "cancelled"; reader.Maximum = 1; };
        reader.Events.Add(new(1, "run.cancelled", 1, "{}", DateTimeOffset.UtcNow));
        var body = await StreamAsync(reader);
        Assert.AreEqual(1, Count(body, "event: RUN_ERROR\n"));
        Assert.AreEqual(1, Count(body, "\"statusKey\":\"running\""), "只有开始快照可以使用取消前的运行态。");
        Assert.Contains("\"statusKey\":\"cancelled\"", body, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Legacy_terminal_without_terminal_event_gets_one_boundary_after_history()
    {
        var reader = new Reader("cancelled", 1);
        reader.Events.Add(new(1, "run.node_started", 1, "{}", DateTimeOffset.UtcNow));
        var body = await StreamAsync(reader);
        Assert.AreEqual(1, Count(body, "event: RUN_ERROR\n"));
        Assert.IsTrue(body.IndexOf("event: RUN_ERROR", StringComparison.Ordinal) > body.IndexOf("event: CUSTOM", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Historical_reconciliation_payload_uses_registered_json_metadata()
    {
        var reader = new Reader("cancelled", 2);
        reader.Events.Add(new(1, "run.reconciliation_required", 1, "{}", DateTimeOffset.UtcNow));
        reader.Events.Add(new(2, "run.cancelled", 1, "{}", DateTimeOffset.UtcNow));
        var body = await StreamAsync(reader);
        Assert.Contains("\"reason\":\"tool_receipt_unknown\"", body, StringComparison.Ordinal);
        Assert.AreEqual(1, Count(body, "event: RUN_ERROR\n"));
    }

    [TestMethod]
    public async Task Resume_always_has_a_connection_start_and_only_one_final_boundary()
    {
        var reader = new Reader("completed", 4);
        foreach (var entry in new[] { (1, "run.node_started"), (2, "run.awaiting_approval"), (3, "run.node_completed"), (4, "run.completed") })
            reader.Events.Add(new(entry.Item1, entry.Item2, 1, "{}", DateTimeOffset.UtcNow));
        var full = await StreamAsync(reader, "history");
        var resumed = await StreamAsync(reader, "resume", 2);
        Assert.AreEqual(1, Count(full, "event: RUN_FINISHED\n"));
        Assert.AreEqual(1, Count(resumed, "event: RUN_STARTED\n"));
        Assert.AreEqual(2, Count(resumed, "event: CUSTOM\n"));
        Assert.IsTrue(resumed.StartsWith("event: RUN_STARTED\n", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("awaiting_approval", "run.awaiting_approval")]
    [DataRow("reconciliation_required", "run.reconciliation_required")]
    public async Task Current_interrupt_closes_the_transport_without_claiming_business_completion(string status, string eventType)
    {
        var reader = new Reader(status, 1);
        reader.Events.Add(new(1, eventType, 1, "{}", DateTimeOffset.UtcNow));
        var body = await StreamAsync(reader, status);
        Assert.AreEqual(1, Count(body, "event: RUN_FINISHED\n"));
        var final = body.Split('\n').Last(line => line.StartsWith("data: ", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(final[6..]);
        Assert.AreEqual("interrupt", json.RootElement.GetProperty("outcome").GetProperty("type").GetString());
    }

    [TestMethod]
    [DataRow(-1L)][DataRow(3L)][DataRow(0L)]
    public async Task Bad_or_missing_cursor_history_is_rejected_before_any_frame(long cursor)
    {
        var reader = new Reader("completed", 2);
        reader.Events.Add(new(2, "run.completed", 1, "{}", DateTimeOffset.UtcNow));
        using var body = new MemoryStream(); var http = new DefaultHttpContext(); http.Response.Body = body;
        var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), cursor, http);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, body.Length);
        Assert.AreEqual("ai.agent_run.events_cursor_expired", result.Error!.Code);
    }

    [TestMethod]
    public async Task Authority_lost_during_event_read_does_not_start_a_private_stream()
    {
        var reader = new Reader("completed", 1); reader.Events.Add(new(1, "run.completed", 1, "{}", DateTimeOffset.UtcNow));
        reader.OnEvents = () => reader.Allowed = false;
        using var body = new MemoryStream(); var http = new DefaultHttpContext(); http.Response.Body = body;
        var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), 0, http);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, body.Length);
    }

    [TestMethod]
    public async Task Authority_lost_after_snapshot_emits_only_a_safe_protocol_error()
    {
        var reader = new Reader("completed", 1); reader.Events.Add(new(1, "run.node_started", 1, "PRIVATE_SENTINEL", DateTimeOffset.UtcNow));
        using var body = new ObservedStream(text => { if (text.Contains("STATE_SNAPSHOT", StringComparison.Ordinal)) reader.Allowed = false; });
        var http = new DefaultHttpContext(); http.Response.Body = body;
        var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), 0, http);
        var text = Encoding.UTF8.GetString(body.ToArray());
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(1, Count(text, "event: RUN_ERROR\n"));
        Assert.IsFalse(text.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("application/problem+json", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Client_cancellation_drains_a_blocked_write_without_changing_the_run()
    {
        var reader = new Reader("completed", 0);
        using var body = new BlockingStream(); var http = new DefaultHttpContext(); http.Response.Body = body;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), 0, http, cancel.Token);
        Assert.IsTrue(result.IsSuccess); Assert.IsFalse(result.Value); Assert.IsTrue(body.Cancelled);
        Assert.AreEqual("completed", reader.Status);
    }

    [TestMethod]
    [DataRow(2, 2)][DataRow(1, 65537)]
    public async Task Unsupported_or_oversized_history_is_rejected_before_start(int version, int length)
    {
        var reader = new Reader("completed", 1);
        reader.Events.Add(new(1, "run.protocol_fixture", version, new string('x', length), DateTimeOffset.UtcNow));
        using var body = new MemoryStream(); var http = new DefaultHttpContext(); http.Response.Body = body;
        var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), 0, http);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual("common.unexpected", result.Error!.Code);
        Assert.AreEqual(0L, body.Length);
    }

    private sealed class ObservedStream(Action<string> written) : MemoryStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        { await base.WriteAsync(buffer, token); written(Encoding.UTF8.GetString(buffer.Span)); }
    }
    private sealed class BlockingStream : MemoryStream
    {
        internal bool Cancelled;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        { try { await Task.Delay(Timeout.InfiniteTimeSpan, token); } catch (OperationCanceledException) { Cancelled = true; throw; } }
    }

    private static int Count(string value, string fragment) => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static async Task<string> StreamAsync(Reader reader, string? corpusName = null, long afterSequence = 0)
    {
        using var buffer = new MemoryStream();
        var http = new DefaultHttpContext(); http.Response.Body = buffer;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), afterSequence, http, cancellation.Token);
        Assert.IsTrue(result.IsSuccess);
        var body = Encoding.UTF8.GetString(buffer.ToArray());
        foreach (var line in body.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal)))
        {
            using var payload = JsonDocument.Parse(line[6..]);
            Assert.IsTrue(payload.RootElement.TryGetProperty("type", out _));
        }
        if (corpusName is not null && Environment.GetEnvironmentVariable("FULLNET_A04_AGUI_CORPUS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, corpusName + ".sse"), body);
            await File.WriteAllTextAsync(Path.Combine(directory, corpusName + ".json"), JsonSerializer.Serialize(new { runId = reader.RunId, threadId = reader.SessionId }));
        }
        return body;
    }

    private sealed class Reader(string status, long maximum) : IAgentRunAgUiReader
    {
        internal readonly Guid RunId = Guid.CreateVersion7(), SessionId = Guid.CreateVersion7();
        internal string Status = status;
        internal long Maximum = maximum;
        internal int EventReads;
        internal Action? OnEvents;
        internal readonly List<AgentRunPersistedEvent> Events = [];
        internal bool Allowed = true;
        public ValueTask<bool> CanReadAsync(Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Allowed);
        public ValueTask<AgentRunAgUiSnapshot?> TryGetOwnedSnapshotAsync(Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AgentRunAgUiSnapshot?>(new(RunId, SessionId, Status, "fullnet-single-text-v1", Maximum));
        public ValueTask<IReadOnlyList<AgentRunPersistedEvent>> ListEventsAfterAsync(Guid runId, long afterSequence, int limit, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventReads++;
            OnEvents?.Invoke();
            return ValueTask.FromResult<IReadOnlyList<AgentRunPersistedEvent>>(Events.Where(e => e.Sequence > afterSequence).Take(limit).ToArray());
        }
        public ValueTask<AgentRunAgUiProgress?> TryGetProgressAsync(Guid runId, string scopeKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AgentRunAgUiProgress?>(null);
    }
}
