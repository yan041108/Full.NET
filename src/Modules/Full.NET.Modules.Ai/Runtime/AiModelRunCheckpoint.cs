using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>单模型或整轮工具循环的持久凭证；started 只证明可能已派发，不能作为自动重试依据。</summary>
internal sealed record AiModelRunCheckpoint(
    int PayloadVersion, Guid RunId, Guid SessionId, string DefinitionKey, int DefinitionVersion,
    Guid OperationId, Guid ModelConfigId, int ModelConfigVersion, string BudgetDigest, string InputDigest,
    string Phase, AgentModelResult? Result = null, string? OutputDigest = null, string? LoopState = null)
{
    internal string ToJson() => JsonSerializer.Serialize(this, AiModelRunCheckpointJsonContext.Default.AiModelRunCheckpoint);

    internal static AiModelRunCheckpoint Start(AiAgentRunRecord run, AiModelConfigRecord model, string prompt) =>
        new(1, run.Id, run.SessionId, run.DefinitionKey, run.DefinitionVersion, run.Id, model.Id, model.Version,
            Digest(run.BudgetJson), Digest(prompt), "started");

    internal AiModelRunCheckpoint Complete(AgentModelResult result) => this with
    {
        Phase = "completed", Result = result, LoopState = null,
        OutputDigest = Digest(JsonSerializer.Serialize(result, AiModelRunCheckpointJsonContext.Default.AgentModelResult))
    };

    /// <summary>逐步状态沿用外层运行、模型与预算绑定；未知派发仍标记 started，不能自动重试。</summary>
    internal AiModelRunCheckpoint WithLoop(string payload)
    {
        var info = AgentToolLoopCheckpoint.Inspect(payload, RunId, DefinitionKey, DefinitionVersion, InputDigest,
            callId => ResolveToolOperationId(RunId, callId));
        return this with { PayloadVersion = 2, Phase = info.ReconciliationRequired ? "started" : "loop_ready",
            LoopState = payload, Result = null, OutputDigest = null };
    }

    internal static Guid ResolveToolOperationId(Guid runId, string callId) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{runId:N}|tool|{callId}"))).AsSpan(0, 16), bigEndian: true);

    /// <summary>校验来源、冻结输入与完整结果；旧消息快照或歧义字段不能被解释为尚未执行。</summary>
    internal static AiModelRunCheckpoint? Restore(AiAgentRunRecord run, AgentRunBudgetSnapshot budget, AgentCheckpointRecord checkpoint)
    {
        if (run.DefinitionKey is not (AgentDefinitionRegistry.SingleTextKey or AgentDefinitionRegistry.ReadOnlyToolLoopKey)
            || checkpoint.RunId != run.Id || checkpoint.CheckpointId == Guid.Empty
            || checkpoint.Sequence <= 0 || checkpoint.Sequence == long.MaxValue
            || checkpoint.DefinitionVersion != run.DefinitionVersion
            || !AgentCheckpointCompatibility.TryValidate(run.DefinitionKey, run.DefinitionVersion,
                checkpoint.FormatVersion, checkpoint.FrameworkVersion, out _)
            || string.IsNullOrWhiteSpace(checkpoint.PayloadProtected)
            || !string.Equals(Digest(checkpoint.PayloadProtected), checkpoint.Checksum, StringComparison.OrdinalIgnoreCase)) return null;
        AiModelRunCheckpoint? state;
        try
        {
            // 外层 JSON 包含转义后的循环正文；先约束解析分配，不能依赖解密后再无限展开。
            if (checkpoint.PayloadProtected.Length > 2097152) return null;
            using var document = JsonDocument.Parse(checkpoint.PayloadProtected);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            // 属性名按解码后的精确文本去重，禁止后置字段覆盖执行阶段或绑定。
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (document.RootElement.EnumerateObject().Any(property => !names.Add(property.Name))) return null;
            state = JsonSerializer.Deserialize(checkpoint.PayloadProtected, AiModelRunCheckpointJsonContext.Default.AiModelRunCheckpoint);
        }
        catch (JsonException) { return null; }
        if (state is null || state.PayloadVersion is not (1 or 2) || state.RunId != run.Id || state.SessionId != run.SessionId
            || state.DefinitionKey != run.DefinitionKey || state.DefinitionVersion != run.DefinitionVersion
            || state.OperationId != run.Id || state.ModelConfigId == Guid.Empty || state.ModelConfigId != budget.ModelConfigId
            || state.ModelConfigVersion <= 0 || state.BudgetDigest != Digest(run.BudgetJson)
            || state.InputDigest is not { Length: 64 } || state.InputDigest.Any(c => !char.IsAsciiHexDigit(c))) return null;
        if (state.PayloadVersion == 1 && state.LoopState is not null
            || state.PayloadVersion == 2 && run.DefinitionKey != AgentDefinitionRegistry.ReadOnlyToolLoopKey) return null;
        if (state.LoopState is not null)
        {
            AgentToolLoopRecoveryInfo info;
            try { info = AgentToolLoopCheckpoint.Inspect(state.LoopState, run.Id, run.DefinitionKey, run.DefinitionVersion,
                state.InputDigest, callId => ResolveToolOperationId(run.Id, callId)); }
            catch (InvalidOperationException) { return null; }
            if (state.Result is not null || state.OutputDigest is not null
                || info.InputTokens > budget.InputTokenLimit || info.OutputTokens > budget.OutputTokenLimit
                || state.Phase != (info.ReconciliationRequired ? "started" : "loop_ready")) return null;
            return state;
        }
        return state.Phase switch
        {
            "started" when state.Result is null && state.OutputDigest is null => state,
            "not_dispatched" when run.DefinitionKey == AgentDefinitionRegistry.SingleTextKey
                && state.Result is null && state.OutputDigest is null => state,
            "completed" when ValidResult(state.Result, budget)
                && state.OutputDigest == Digest(JsonSerializer.Serialize(state.Result, AiModelRunCheckpointJsonContext.Default.AgentModelResult)) => state,
            _ => null
        };
    }

    internal static bool ValidResult(AgentModelResult? result, AgentRunBudgetSnapshot budget) =>
        result is { Text: not null } && result.Session.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
        && result.InputTokens is not < 0 && !(result.InputTokens > budget.InputTokenLimit)
        && result.OutputTokens is not < 0 && !(result.OutputTokens > budget.OutputTokenLimit);

    internal static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // 检查点包含正文与会话，禁止 record 的默认诊断字符串将它们写入日志。
    public override string ToString() => $"AiModelRunCheckpoint {{ RunId = {RunId}, Phase = {Phase} }}";
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AiModelRunCheckpoint))]
[JsonSerializable(typeof(AgentModelResult))]
internal sealed partial class AiModelRunCheckpointJsonContext : JsonSerializerContext;
