using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>单模型一次派发的持久凭证；started 只证明可能已派发，不能作为自动重试依据。</summary>
internal sealed record AiSingleModelCheckpoint(
    int PayloadVersion, Guid RunId, Guid SessionId, string DefinitionKey, int DefinitionVersion,
    Guid OperationId, Guid ModelConfigId, int ModelConfigVersion, string BudgetDigest, string InputDigest,
    string Phase, AgentModelResult? Result = null, string? OutputDigest = null)
{
    internal string ToJson() => JsonSerializer.Serialize(this, AiSingleModelCheckpointJsonContext.Default.AiSingleModelCheckpoint);

    internal static AiSingleModelCheckpoint Start(AiAgentRunRecord run, AiModelConfigRecord model, string prompt) =>
        new(1, run.Id, run.SessionId, run.DefinitionKey, run.DefinitionVersion, run.Id, model.Id, model.Version,
            Digest(run.BudgetJson), Digest(prompt), "started");

    internal AiSingleModelCheckpoint Complete(AgentModelResult result) => this with
    {
        Phase = "completed", Result = result,
        OutputDigest = Digest(JsonSerializer.Serialize(result, AiSingleModelCheckpointJsonContext.Default.AgentModelResult))
    };

    /// <summary>校验来源、冻结输入与完整结果；旧消息快照或歧义字段不能被解释为尚未执行。</summary>
    internal static AiSingleModelCheckpoint? Restore(AiAgentRunRecord run, AgentRunBudgetSnapshot budget, AgentCheckpointRecord checkpoint)
    {
        if (run.DefinitionKey != AgentDefinitionRegistry.SingleTextKey
            || checkpoint.RunId != run.Id || checkpoint.CheckpointId == Guid.Empty
            || checkpoint.Sequence <= 0 || checkpoint.Sequence == long.MaxValue
            || checkpoint.DefinitionVersion != run.DefinitionVersion
            || !AgentCheckpointCompatibility.TryValidate(run.DefinitionKey, run.DefinitionVersion,
                checkpoint.FormatVersion, checkpoint.FrameworkVersion, out _)
            || string.IsNullOrWhiteSpace(checkpoint.PayloadProtected)
            || !string.Equals(Digest(checkpoint.PayloadProtected), checkpoint.Checksum, StringComparison.OrdinalIgnoreCase)) return null;
        AiSingleModelCheckpoint? state;
        try
        {
            using var document = JsonDocument.Parse(checkpoint.PayloadProtected);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            // 属性名按解码后的精确文本去重，禁止后置字段覆盖执行阶段或绑定。
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (document.RootElement.EnumerateObject().Any(property => !names.Add(property.Name))) return null;
            state = JsonSerializer.Deserialize(checkpoint.PayloadProtected, AiSingleModelCheckpointJsonContext.Default.AiSingleModelCheckpoint);
        }
        catch (JsonException) { return null; }
        if (state is null || state.PayloadVersion != 1 || state.RunId != run.Id || state.SessionId != run.SessionId
            || state.DefinitionKey != run.DefinitionKey || state.DefinitionVersion != run.DefinitionVersion
            || state.OperationId != run.Id || state.ModelConfigId == Guid.Empty || state.ModelConfigId != budget.ModelConfigId
            || state.ModelConfigVersion <= 0 || state.BudgetDigest != Digest(run.BudgetJson)
            || state.InputDigest is not { Length: 64 } || state.InputDigest.Any(c => !char.IsAsciiHexDigit(c))) return null;
        return state.Phase switch
        {
            "started" or "not_dispatched" when state.Result is null && state.OutputDigest is null => state,
            "completed" when ValidResult(state.Result, budget)
                && state.OutputDigest == Digest(JsonSerializer.Serialize(state.Result, AiSingleModelCheckpointJsonContext.Default.AgentModelResult)) => state,
            _ => null
        };
    }

    internal static bool ValidResult(AgentModelResult? result, AgentRunBudgetSnapshot budget) =>
        result is { Text: not null } && result.Session.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
        && result.InputTokens is not < 0 && !(result.InputTokens > budget.InputTokenLimit)
        && result.OutputTokens is not < 0 && !(result.OutputTokens > budget.OutputTokenLimit);

    internal static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // 检查点包含正文与会话，禁止 record 的默认诊断字符串将它们写入日志。
    public override string ToString() => $"AiSingleModelCheckpoint {{ RunId = {RunId}, Phase = {Phase} }}";
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AiSingleModelCheckpoint))]
[JsonSerializable(typeof(AgentModelResult))]
internal sealed partial class AiSingleModelCheckpointJsonContext : JsonSerializerContext;
