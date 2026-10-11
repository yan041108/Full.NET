using System.Text.Json.Serialization;

namespace Full.NET.AgenticWeb.AgUi.Serialization;

[JsonSerializable(typeof(AgUiRunStartedPayload))]
[JsonSerializable(typeof(AgUiRunFinishedPayload))]
[JsonSerializable(typeof(AgUiRunErrorPayload))]
[JsonSerializable(typeof(AgUiStateSnapshotPayload))]
[JsonSerializable(typeof(AgUiCustomPayload))]
[JsonSerializable(typeof(AgUiPersistedEventValue))]
[JsonSerializable(typeof(AgUiReconciliationValue))]
[JsonSerializable(typeof(AgUiRunProgressState))]
[JsonSerializable(typeof(AgUiRunStepState))]
[JsonSerializable(typeof(AgUiRunBudgetState))]
internal sealed partial class AgUiJsonSerializerContext : JsonSerializerContext;

internal sealed record AgUiRunStartedPayload(string Type, string ThreadId, string RunId);

internal sealed record AgUiRunFinishedPayload(
    string Type,
    string ThreadId,
    string RunId,
    AgUiRunFinishedOutcome? Outcome = null,
    object? Result = null);

internal sealed record AgUiRunFinishedOutcome(string Type, IReadOnlyList<AgUiRunInterrupt>? Interrupts = null);

internal sealed record AgUiRunInterrupt(string Id, string Reason);

internal sealed record AgUiRunErrorPayload(string Type, string Message, string? Code = null);

internal sealed record AgUiStateSnapshotPayload(string Type, AgUiRunProgressState Snapshot);

internal sealed record AgUiRunProgressState(
    string StatusKey,
    string DefinitionKey,
    AgUiRunBudgetState? Budget,
    IReadOnlyList<AgUiRunStepState> Steps);

internal sealed record AgUiRunBudgetState(
    long? InputTokens,
    long? OutputTokens,
    string? UsageStatus,
    string? Outcome);

internal sealed record AgUiRunStepState(
    string StepKey,
    int Attempt,
    string StatusKey,
    long? InputTokens,
    long? OutputTokens,
    string? ErrorCode);

internal sealed record AgUiCustomPayload(string Type, string Name, object Value);

/// <summary>持久事件的具体值类型，供源生成序列化静态解析，保留已有 JSON 字段。</summary>
internal sealed record AgUiPersistedEventValue(Guid RunId, long Sequence, int PayloadVersion, string Payload);

/// <summary>对账事件只发送固定原因，不携带外部工具回执正文。</summary>
internal sealed record AgUiReconciliationValue(Guid RunId, long Sequence, string Reason);
