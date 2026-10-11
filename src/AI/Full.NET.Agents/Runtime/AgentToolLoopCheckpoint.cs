using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Agents.Definitions;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Runtime;

/// <summary>工具循环检查点的封闭恢复准入；正文由调用方在有租约保护的存储中保存。</summary>
public static class AgentToolLoopCheckpoint
{
    internal const int MaximumPayloadCharacters = 1048576;

    /// <summary>在创建模型客户端前验证完整轮次、已完成前缀、绑定和累计用量；不派发外部请求。</summary>
    /// <param name="payload">已解密且校验外层存储摘要的检查点 JSON。</param>
    /// <param name="runId">当前可信运行标识。</param>
    /// <param name="definitionKey">当前冻结的定义键。</param>
    /// <param name="definitionVersion">当前冻结的定义版本。</param>
    /// <param name="inputDigest">有效提示词的 SHA256 十六进制摘要。</param>
    /// <param name="resolveOperationId">核对原回执操作标识的纯函数；持久化消费者必须提供。</param>
    /// <returns>实际累计用量与恢复阶段；未知派发或暂停阶段必须交由对账。</returns>
    /// <exception cref="InvalidOperationException">载荷不兼容、损坏或违反恢复不变量。</exception>
    public static AgentToolLoopRecoveryInfo Inspect(string payload, Guid runId, string definitionKey,
        int definitionVersion, string inputDigest, Func<string, Guid>? resolveOperationId = null)
    {
        var state = Restore(payload, runId, definitionKey, definitionVersion, inputDigest, resolveOperationId);
        return Summarize(state);
    }

    /// <summary>复用最终模型回执生成既有结果；不创建客户端、不重发模型或工具。</summary>
    /// <param name="payload">经存储校验的检查点 JSON。</param>
    /// <param name="runId">可信运行标识。</param>
    /// <param name="definitionKey">冻结定义键。</param>
    /// <param name="definitionVersion">冻结定义版本。</param>
    /// <param name="prompt">重新解析并验证的有效提示词。</param>
    /// <param name="resolveOperationId">核对原回执操作标识的纯函数。</param>
    /// <returns>已完成结果；尚未完成时返回 null。</returns>
    public static AgentToolLoopResult? ReadCompletedResult(string payload, Guid runId, string definitionKey,
        int definitionVersion, string prompt, Func<string, Guid>? resolveOperationId = null)
    {
        var state = Restore(payload, runId, definitionKey, definitionVersion, Digest(prompt), resolveOperationId);
        return state.Phase == "completed" ? CompletedResult(state, prompt) : null;
    }

    internal static AgentToolLoopResult CompletedResult(AgentToolLoopState state, string prompt)
    {
        var text = BuildAssistantMessages(state.Rounds[^1]).LastOrDefault()?.Text ?? string.Empty;
        var info = Summarize(state);
        return new(text, AgentToolLoop.SerializeSession(History(state, prompt, omitFinal: true), text),
            info.InputTokens, info.OutputTokens, info.ToolSteps);
    }

    internal static AgentToolLoopState Restore(string payload, Guid runId, string key, int version,
        string digest, Func<string, Guid>? resolve)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumPayloadCharacters) Invalid();
        AgentToolLoopState? state;
        try
        {
            using var document = JsonDocument.Parse(payload, new() { MaxDepth = 48 });
            var nodes = 0; ValidateJson(document.RootElement, ref nodes);
            state = JsonSerializer.Deserialize(payload, AgentToolLoopCheckpointJsonContext.Default.AgentToolLoopState);
        }
        catch (JsonException) { Invalid(); return null!; }
        var definition = AgentDefinitionRegistry.Resolve(key, version);
        if (state is null || definition is null || definition.MaxToolIterations <= 0 || runId == Guid.Empty
            || state.Version != 1 || state.RunId != runId || state.DefinitionKey != key || state.DefinitionVersion != version
            || state.InputDigest != digest || digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigit(c))
            || state.Rounds is null || state.Rounds.Count > definition.MaxToolIterations
            || state.Phase is not ("model_pending" or "tool_pending" or "tools_ready" or "completed" or "blocked")) Invalid();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var steps = 0; var unresolved = 0; var pending = 0; var paused = 0;
        for (var index = 0; index < state!.Rounds.Count; index++)
        {
            var round = state.Rounds[index];
            if (round is null || round.Messages is null || round.InputTokens is < 0 || round.OutputTokens is < 0) Invalid();
            steps++;
            var response = new ChatResponse(BuildAssistantMessages(round!));
            var admitted = AgentToolCallAdmission.Freeze(response, 64 - steps, ids, CancellationToken.None);
            var calls = Calls(round!).ToArray();
            if (calls.Length != admitted.Calls.Count || index < state.Rounds.Count - 1 && calls.Length == 0) Invalid();
            var missing = false;
            foreach (var call in calls)
            {
                if (call.ToolVersion is <= 0 || call.ToolVersion is null && call.OperationId is not null) Invalid();
                if (call.OperationId is { } operation
                    && (operation == Guid.Empty || resolve is not null && resolve(call.CallId!) != operation)) Invalid();
                if (call.ResultPayload is null)
                {
                    missing = true; unresolved++;
                    if (call.OperationId.HasValue) { pending++; steps++; }
                    continue;
                }
                if (missing || call.ToolVersion.HasValue && !call.OperationId.HasValue) Invalid();
                var result = ReadResult(call.ResultPayload);
                if (call.ToolVersion is null && (result.StatusKey != "denied" || result.ErrorCode != "ai.tool.unavailable")) Invalid();
                if (IsPause(result.ErrorCode)) paused++;
                steps++;
            }
            if (index < state.Rounds.Count - 1 && (missing || paused != 0)) Invalid();
        }
        var lastCalls = state.Rounds.Count == 0 ? [] : Calls(state.Rounds[^1]).ToArray();
        if (steps > 64 || pending > 1 || paused > 1
            || state.Phase == "model_pending" && (unresolved != 0 || paused != 0 || steps >= 64 || state.Rounds.Count >= definition!.MaxToolIterations)
            || state.Phase == "tool_pending" && (pending != 1 || unresolved == 0 || paused != 0)
            || state.Phase == "tools_ready" && (pending != 0 || paused != 0 || state.Rounds.Count == 0 || lastCalls.Length == 0)
            || state.Phase == "completed" && (unresolved != 0 || pending != 0 || paused != 0 || state.Rounds.Count == 0 || lastCalls.Length != 0)
            || state.Phase == "blocked" && (paused != 1 || pending != 0)) Invalid();
        // 未完成调用只能位于最后轮；有派发标识的未完成调用只能是当前游标，不能跳过前面的空回执。
        if (pending != 0 && lastCalls.FirstOrDefault(c => c.ResultPayload is null)?.OperationId is null) Invalid();
        return state;
    }

    internal static string Serialize(AgentToolLoopState state)
    {
        var value = JsonSerializer.Serialize(state, AgentToolLoopCheckpointJsonContext.Default.AgentToolLoopState);
        if (value.Length > MaximumPayloadCharacters) Invalid();
        return value;
    }

    internal static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static IEnumerable<AgentToolLoopContent> Calls(AgentToolLoopRound round) =>
        round.Messages.SelectMany(m => m.Contents).Where(c => c.Kind == "call");

    internal static List<ChatMessage> BuildAssistantMessages(AgentToolLoopRound round)
    {
        if (round.Messages.Count > 128) Invalid();
        var result = new List<ChatMessage>();
        foreach (var message in round.Messages)
        {
            if (message is null || message.Contents is null || message.Contents.Count > 512) Invalid();
            var contents = new List<AIContent>();
            foreach (var content in message!.Contents)
            {
                if (content is null) Invalid();
                if (content!.Kind == "text")
                {
                    if (content.CallId is not null || content.Name is not null || content.Arguments.HasValue
                        || content.ToolVersion.HasValue || content.OperationId.HasValue || content.ResultPayload is not null) Invalid();
                    contents.Add(new TextContent(content.Text));
                }
                else if (content.Kind == "call")
                {
                    if (content.Text is not null || content.Arguments is not { ValueKind: JsonValueKind.Object }) Invalid();
                    var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var property in content.Arguments!.Value.EnumerateObject()) arguments.Add(property.Name, property.Value);
                    contents.Add(new FunctionCallContent(content.CallId!, content.Name!, arguments));
                }
                else Invalid();
            }
            result.Add(new(ChatRole.Assistant, contents) { AuthorName = message.AuthorName, MessageId = message.MessageId, CreatedAt = message.CreatedAt });
        }
        return result;
    }

    internal static List<ChatMessage> History(AgentToolLoopState state, string prompt, bool omitFinal = false)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var rounds = omitFinal && state.Phase == "completed" ? state.Rounds.Take(state.Rounds.Count - 1) : state.Rounds;
        foreach (var round in rounds)
        {
            messages.AddRange(BuildAssistantMessages(round));
            foreach (var call in Calls(round).TakeWhile(c => c.ResultPayload is not null))
                messages.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId!, call.ResultPayload)]));
        }
        return messages;
    }

    internal static AgentToolLoopRecoveryInfo Summarize(AgentToolLoopState state)
    {
        long? input = null, output = null; var attempts = 0;
        for (var index = 0; index < state.Rounds.Count; index++)
        {
            input = AgentTokenUsage.Add(input, state.Rounds[index].InputTokens, index == 0);
            output = AgentTokenUsage.Add(output, state.Rounds[index].OutputTokens, index == 0);
            attempts += Calls(state.Rounds[index]).Count(c => c.ResultPayload is not null || c.OperationId.HasValue);
        }
        return new(state.Phase, input, output, attempts,
            state.Rounds.Count + attempts + (state.Phase == "model_pending" ? 1 : 0), state.Rounds.Count,
            state.Phase is "model_pending" or "tool_pending" or "blocked");
    }

    internal static ToolLoopExecutionPayload ReadResult(string payload)
    {
        // 执行器按原 JSON 的 UTF-8 字节限制结果；默认编码器每字符至多扩展为六个 ASCII 字符。
        if (payload.Length > 6 * 65536 + 512) Invalid();
        try
        {
            using var document = JsonDocument.Parse(payload, new() { MaxDepth = 32 });
            var nodes = 0; ValidateJson(document.RootElement, ref nodes);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Any(p => p.Name is not ("statusKey" or "errorCode" or "value" or "isUntrusted"))) Invalid();
            var result = JsonSerializer.Deserialize(payload, AgentToolLoopJsonSerializerContext.Default.ToolLoopExecutionPayload);
            // 合法 Handler 可返回 JSON null；可空 JsonElement 无法区分显式空值和缺失字段，须核对原文。
            if (result is null || result.StatusKey is not ("succeeded" or "denied" or "failed" or "cancelled")
                || result.StatusKey == "succeeded" && (result.ErrorCode is not null || !document.RootElement.TryGetProperty("value", out _))
                || result.StatusKey != "succeeded" && string.IsNullOrWhiteSpace(result.ErrorCode)) Invalid();
            return result!;
        }
        catch (JsonException) { Invalid(); return null!; }
    }

    internal static bool IsPause(string? code) => code is "ai.tool.approval_required" or "ai.tool.reconciliation_required";
    private static void ValidateJson(JsonElement element, ref int nodes)
    {
        if (++nodes > 65536) Invalid();
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) Invalid(); ValidateJson(property.Value, ref nodes); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateJson(child, ref nodes);
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void Invalid() => throw new InvalidOperationException("Invalid agent tool loop checkpoint.");
}

/// <summary>经过完整准入的循环恢复摘要；空用量表示供应商未知，不得补零授予新额度。</summary>
/// <param name="Phase">封闭的恢复阶段。</param>
/// <param name="InputTokens">已确认输入用量或未知。</param>
/// <param name="OutputTokens">已确认输出用量或未知。</param>
/// <param name="ToolSteps">包含拒绝项和当前派发意图的工具步数。</param>
/// <param name="ExecutionSteps">模型与工具累计步数。</param>
/// <param name="ModelCalls">已确认模型轮次。</param>
/// <param name="ReconciliationRequired">只有意图或暂停，不能自动续跑。</param>
public sealed record AgentToolLoopRecoveryInfo(string Phase, long? InputTokens, long? OutputTokens,
    int ToolSteps, int ExecutionSteps, int ModelCalls, bool ReconciliationRequired);

// 持久化只使用自有具体 DTO，不序列化 SDK 开放对象；这些正文类型禁止默认诊断输出。
internal sealed class AgentToolLoopState
{
    public int Version { get; set; } = 1;
    public Guid RunId { get; set; }
    public string DefinitionKey { get; set; } = null!;
    public int DefinitionVersion { get; set; }
    public string InputDigest { get; set; } = null!;
    public string Phase { get; set; } = "tools_ready";
    public List<AgentToolLoopRound> Rounds { get; set; } = [];
    public override string ToString() => nameof(AgentToolLoopState);
}
internal sealed class AgentToolLoopRound
{
    public List<AgentToolLoopMessage> Messages { get; set; } = [];
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public override string ToString() => nameof(AgentToolLoopRound);
}
internal sealed class AgentToolLoopMessage
{
    public List<AgentToolLoopContent> Contents { get; set; } = [];
    public string? AuthorName { get; set; }
    public string? MessageId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public override string ToString() => nameof(AgentToolLoopMessage);
}
internal sealed class AgentToolLoopContent
{
    public string Kind { get; set; } = null!;
    public string? Text { get; set; }
    public string? CallId { get; set; }
    public string? Name { get; set; }
    public JsonElement? Arguments { get; set; }
    public int? ToolVersion { get; set; }
    public Guid? OperationId { get; set; }
    public string? ResultPayload { get; set; }
    public override string ToString() => nameof(AgentToolLoopContent);
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AgentToolLoopState))]
internal sealed partial class AgentToolLoopCheckpointJsonContext : JsonSerializerContext;
