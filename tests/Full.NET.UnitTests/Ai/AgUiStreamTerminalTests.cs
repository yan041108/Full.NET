using System.Text;
using System.Text.Json;
using Full.NET.Agents.AgUi;
using Full.NET.AgenticWeb.AgUi;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Ai;

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
        var body = await StreamAsync(reader);
        Assert.AreEqual(2, reader.EventReads);
        Assert.AreEqual(100, Count(body, "event: CUSTOM\n"));
        Assert.AreEqual(1, Count(body, status == "completed" ? "event: RUN_FINISHED\n" : "event: RUN_ERROR\n"));
        Assert.IsTrue(body.LastIndexOf("event: STATE_SNAPSHOT", StringComparison.Ordinal) > body.LastIndexOf("event: CUSTOM", StringComparison.Ordinal));
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

    private static int Count(string value, string fragment) => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static async Task<string> StreamAsync(Reader reader)
    {
        using var buffer = new MemoryStream();
        var http = new DefaultHttpContext(); http.Response.Body = buffer;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), 0, http, cancellation.Token);
        Assert.IsTrue(result.IsSuccess);
        var body = Encoding.UTF8.GetString(buffer.ToArray());
        foreach (var line in body.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal)))
        {
            using var payload = JsonDocument.Parse(line[6..]);
            Assert.IsTrue(payload.RootElement.TryGetProperty("type", out _));
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
