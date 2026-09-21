using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Full.NET.Agents.AgUi;
using Full.NET.AgenticWeb.AgUi.Serialization;

namespace Full.NET.AgenticWeb.AgUi;

/// <summary>将 fn_ai_agent_event 持久类型映射为标准 AG-UI 生命周期事件。</summary>
public static class AgUiEventMapper
{
    private static readonly AgUiJsonSerializerContext SerializerContext = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>
    /// 构造 AG-UI run.started 事件，携带会话与运行标识。
    /// </summary>
    /// <param name="snapshot">当前运行快照，提供 SessionId 与 RunId。</param>
    /// <returns>可直接序列化下发的 AG-UI 映射事件。</returns>
    public static AgUiMappedEvent CreateRunStarted(AgentRunAgUiSnapshot snapshot) =>
        new(
            AgUiProtocolEventTypes.RunStarted,
            new AgUiRunStartedPayload(
                AgUiProtocolEventTypes.RunStarted,
                snapshot.SessionId.ToString("D"),
                snapshot.RunId.ToString("D")),
            SerializerContext.AgUiRunStartedPayload);

    /// <summary>
    /// 构造 AG-UI state.snapshot 事件，将运行状态、预算与步骤列表序列化为标准状态快照。
    /// </summary>
    /// <param name="snapshot">当前运行快照，提供状态键与定义键。</param>
    /// <param name="progress">可选进度；为 null 时不输出预算与步骤。</param>
    /// <returns>包含完整运行状态的 AG-UI 映射事件。</returns>
    public static AgUiMappedEvent CreateStateSnapshot(
        AgentRunAgUiSnapshot snapshot,
        AgentRunAgUiProgress? progress) =>
        new(
            AgUiProtocolEventTypes.StateSnapshot,
            new AgUiStateSnapshotPayload(
                AgUiProtocolEventTypes.StateSnapshot,
                new AgUiRunProgressState(
                    snapshot.StatusKey,
                    snapshot.DefinitionKey,
                    progress is null
                        ? null
                        : new AgUiRunBudgetState(
                            progress.InputTokens,
                            progress.OutputTokens,
                            progress.UsageStatus,
                            progress.Outcome),
                    progress?.Steps.Select(step => new AgUiRunStepState(
                        step.StepKey,
                        step.Attempt,
                        step.StatusKey,
                        step.InputTokens,
                        step.OutputTokens,
                        step.ErrorCode)).ToArray() ?? [])),
            SerializerContext.AgUiStateSnapshotPayload);

    /// <summary>
    /// 将持久化事件映射为一个或多个 AG-UI 协议事件；未知事件类型透传为 custom。
    /// </summary>
    /// <param name="snapshot">当前运行快照，用于补全会话与运行标识。</param>
    /// <param name="persisted">持久化事件，含事件类型、序号与载荷。</param>
    /// <returns>按协议顺序产出的 AG-UI 映射事件流。</returns>
    public static IEnumerable<AgUiMappedEvent> MapPersistedEvent(
        AgentRunAgUiSnapshot snapshot,
        AgentRunPersistedEvent persisted)
    {
        switch (persisted.EventType)
        {
            case "run.completed":
                yield return new AgUiMappedEvent(
                    AgUiProtocolEventTypes.RunFinished,
                    new AgUiRunFinishedPayload(
                        AgUiProtocolEventTypes.RunFinished,
                        snapshot.SessionId.ToString("D"),
                        snapshot.RunId.ToString("D"),
                        new AgUiRunFinishedOutcome("success")),
                    SerializerContext.AgUiRunFinishedPayload);
                break;
            case "run.failed":
                yield return MapRunFailed(snapshot, persisted.Payload);
                break;
            case "run.awaiting_approval":
                yield return new AgUiMappedEvent(
                    AgUiProtocolEventTypes.RunFinished,
                    new AgUiRunFinishedPayload(
                        AgUiProtocolEventTypes.RunFinished,
                        snapshot.SessionId.ToString("D"),
                        snapshot.RunId.ToString("D"),
                        new AgUiRunFinishedOutcome(
                            "interrupt",
                            [new AgUiRunInterrupt("approval", "tool_approval_required")])),
                    SerializerContext.AgUiRunFinishedPayload);
                break;
            case "run.authorization_required":
                yield return MapRunFailed(snapshot, persisted.Payload, "authorization_required");
                break;
            case "run.reconciliation_required":
                yield return new AgUiMappedEvent(
                    AgUiProtocolEventTypes.Custom,
                    new AgUiCustomPayload(
                        AgUiProtocolEventTypes.Custom,
                        "fullnet.run.reconciliation_required",
                        new { snapshot.RunId, persisted.Sequence, reason = "tool_receipt_unknown" }),
                    SerializerContext.AgUiCustomPayload);
                yield return new AgUiMappedEvent(
                    AgUiProtocolEventTypes.RunFinished,
                    new AgUiRunFinishedPayload(
                        AgUiProtocolEventTypes.RunFinished,
                        snapshot.SessionId.ToString("D"),
                        snapshot.RunId.ToString("D"),
                        new AgUiRunFinishedOutcome("success")),
                    SerializerContext.AgUiRunFinishedPayload);
                break;
            default:
                yield return new AgUiMappedEvent(
                    AgUiProtocolEventTypes.Custom,
                    new AgUiCustomPayload(
                        AgUiProtocolEventTypes.Custom,
                        persisted.EventType,
                        new { snapshot.RunId, persisted.Sequence, persisted.PayloadVersion, persisted.Payload }),
                    SerializerContext.AgUiCustomPayload);
                break;
        }
    }

    /// <summary>
    /// 根据快照终态构造 AG-UI 终止边界事件，保证前端收到明确的 run.finished/run.error。
    /// </summary>
    /// <param name="snapshot">当前运行快照，提供终态 StatusKey。</param>
    /// <param name="includeWhenNoPersistedEvent">true 时即使无持久化事件也输出边界；false 且已有事件时返回 null。</param>
    /// <returns>终止边界事件；非终态或无需补边界时返回 null。</returns>
    public static AgUiMappedEvent? CreateTerminalBoundary(
        AgentRunAgUiSnapshot snapshot,
        bool includeWhenNoPersistedEvent)
    {
        if (!includeWhenNoPersistedEvent && snapshot.MaxEventSequence > 0)
        {
            return null;
        }

        return snapshot.StatusKey switch
        {
            "completed" => new AgUiMappedEvent(
                AgUiProtocolEventTypes.RunFinished,
                new AgUiRunFinishedPayload(
                    AgUiProtocolEventTypes.RunFinished,
                    snapshot.SessionId.ToString("D"),
                    snapshot.RunId.ToString("D"),
                    new AgUiRunFinishedOutcome("success")),
                SerializerContext.AgUiRunFinishedPayload),
            "failed" or "cancelled" or "expired" => MapRunFailed(
                snapshot,
                """{"errorCode":"ai.agent_run.terminal"}""",
                snapshot.StatusKey),
            "awaiting_approval" => new AgUiMappedEvent(
                AgUiProtocolEventTypes.RunFinished,
                new AgUiRunFinishedPayload(
                    AgUiProtocolEventTypes.RunFinished,
                    snapshot.SessionId.ToString("D"),
                    snapshot.RunId.ToString("D"),
                    new AgUiRunFinishedOutcome(
                        "interrupt",
                        [new AgUiRunInterrupt("approval", "tool_approval_required")])),
                SerializerContext.AgUiRunFinishedPayload),
            _ => null
        };
    }

    private static AgUiMappedEvent MapRunFailed(
        AgentRunAgUiSnapshot snapshot,
        string payload,
        string? fallbackCode = null)
    {
        string? code = fallbackCode;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("errorCode", out var errorCode)
                && errorCode.ValueKind == JsonValueKind.String)
            {
                code = errorCode.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return new AgUiMappedEvent(
            AgUiProtocolEventTypes.RunError,
            new AgUiRunErrorPayload(
                AgUiProtocolEventTypes.RunError,
                "Agent run failed.",
                code),
            SerializerContext.AgUiRunErrorPayload);
    }
}

/// <summary>
/// AG-UI 映射事件的不可变三元组：协议事件类型、强类型载荷与对应的 JSON 序列化元数据。
/// </summary>
/// <param name="EventType">AG-UI 协议事件类型常量。</param>
/// <param name="Payload">与 EventType 对应的强类型载荷对象。</param>
/// <param name="TypeInfo">JSON Source Generator 元数据，用于无反射序列化。</param>
public sealed record AgUiMappedEvent(string EventType, object Payload, JsonTypeInfo TypeInfo);
