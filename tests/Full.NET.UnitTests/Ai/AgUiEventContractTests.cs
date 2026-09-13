using Full.NET.Agents.AgUi;
using Full.NET.AgenticWeb.AgUi;

namespace Full.NET.UnitTests.Ai;

/// <summary>持久事件映射必须输出标准 AG-UI 生命周期事件名，不能把自定义名称冒充协议事件。</summary>
[TestClass]
public sealed class AgUiEventContractTests
{
    private static readonly AgentRunAgUiSnapshot Snapshot = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "running",
        "fullnet-single-text-v1",
        1);

    [TestMethod]
    public void Run_started_uses_standard_event_type()
    {
        var mapped = AgUiEventMapper.CreateRunStarted(Snapshot);
        Assert.AreEqual(AgUiProtocolEventTypes.RunStarted, mapped.EventType);
        Assert.IsTrue(mapped.Payload.ToString()!.Contains(AgUiProtocolEventTypes.RunStarted, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Completed_persisted_event_maps_to_run_finished_success()
    {
        var events = AgUiEventMapper.MapPersistedEvent(
            Snapshot,
            new AgentRunPersistedEvent(1, "run.completed", 1, """{"status":"completed"}""", DateTimeOffset.UtcNow))
            .ToArray();
        Assert.AreEqual(1, events.Length);
        Assert.AreEqual(AgUiProtocolEventTypes.RunFinished, events[0].EventType);
    }

    [TestMethod]
    public void Failed_persisted_event_maps_to_run_error()
    {
        var events = AgUiEventMapper.MapPersistedEvent(
            Snapshot,
            new AgentRunPersistedEvent(1, "run.failed", 1, """{"errorCode":"ai.agent_run.failed"}""", DateTimeOffset.UtcNow))
            .ToArray();
        Assert.AreEqual(1, events.Length);
        Assert.AreEqual(AgUiProtocolEventTypes.RunError, events[0].EventType);
    }

    [TestMethod]
    public void Awaiting_approval_maps_to_interrupt_finished_outcome()
    {
        var events = AgUiEventMapper.MapPersistedEvent(
            Snapshot with { StatusKey = "awaiting_approval" },
            new AgentRunPersistedEvent(1, "run.awaiting_approval", 1, """{"reason":"tool_approval_required"}""", DateTimeOffset.UtcNow))
            .ToArray();
        Assert.AreEqual(AgUiProtocolEventTypes.RunFinished, events[0].EventType);
        Assert.IsTrue(events[0].Payload.ToString()!.Contains("interrupt", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Lifecycle_order_starts_with_run_started_before_terminal_mapping()
    {
        var started = AgUiEventMapper.CreateRunStarted(Snapshot);
        var finished = AgUiEventMapper.MapPersistedEvent(
            Snapshot with { StatusKey = "completed" },
            new AgentRunPersistedEvent(1, "run.completed", 1, """{"status":"completed"}""", DateTimeOffset.UtcNow))
            .Single();
        Assert.AreEqual(AgUiProtocolEventTypes.RunStarted, started.EventType);
        Assert.AreEqual(AgUiProtocolEventTypes.RunFinished, finished.EventType);
    }
}
