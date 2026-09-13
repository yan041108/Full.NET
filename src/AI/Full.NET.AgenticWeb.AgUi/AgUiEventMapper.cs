using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Full.NET.Agents.AgUi;
using Full.NET.AgenticWeb.AgUi.Serialization;

namespace Full.NET.AgenticWeb.AgUi;

/// <summary>将 fn_ai_agent_event 持久类型映射为标准 AG-UI 生命周期事件。</summary>
public static class AgUiEventMapper
{
    private static readonly AgUiJsonSerializerContext SerializerContext = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static AgUiMappedEvent CreateRunStarted(AgentRunAgUiSnapshot snapshot) =>
        new(
            AgUiProtocolEventTypes.RunStarted,
            new AgUiRunStartedPayload(
                AgUiProtocolEventTypes.RunStarted,
                snapshot.SessionId.ToString("D"),
                snapshot.RunId.ToString("D")),
            SerializerContext.AgUiRunStartedPayload);

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

public sealed record AgUiMappedEvent(string EventType, object Payload, JsonTypeInfo TypeInfo);
