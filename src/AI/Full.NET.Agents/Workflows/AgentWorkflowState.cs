using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Agents.Workflows;

/// <summary>工作流检查点状态；在节点边界持久化。</summary>
public sealed class AgentWorkflowState
{
    /// <summary>下一待执行节点索引。</summary>
    public int NextNodeIndex { get; set; }

    /// <summary>已完成节点输出。</summary>
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>关联会话标识。</summary>
    public Guid SessionId { get; set; }

    /// <summary>累计输入 Token。</summary>
    public long? InputTokens { get; set; }

    /// <summary>累计输出 Token。</summary>
    public long? OutputTokens { get; set; }

    /// <summary>新节点凭证的执行绑定；旧检查点为空，不能补造其历史凭证。</summary>
    public AgentWorkflowExecutionBinding? Binding { get; set; }

    /// <summary>已确定成功的节点凭证，与检查点及步骤在同一事务提交。</summary>
    public List<AgentWorkflowNodeReceipt> NodeReceipts { get; set; } = [];

    /// <summary>已持久化的派发意图；存在时恢复必须对账，不能推断尚未调用。</summary>
    public AgentWorkflowNodeReceipt? PendingNode { get; set; }

    /// <summary>累计节点尝试步数，包含审批拒绝；旧检查点缺失时保持未知，不推算为零。</summary>
    public int? ExecutionSteps { get; set; }

    /// <summary>序列化为检查点 JSON。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, AgentWorkflowJsonContext.Default.AgentWorkflowState);

    /// <summary>从检查点 JSON 反序列化。</summary>
    /// <param name="json">原始检查点载荷；缺失累计步数保持未知。</param>
    /// <returns>工作流状态；JSON null返回null。</returns>
    /// <exception cref="JsonException">JSON损坏、计数类型无效或累计步数字段重复。</exception>
    public static AgentWorkflowState? FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        // 同名计数不能采用最后一个值覆盖，避免耗尽额度被后置字段重置。
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.EnumerateObject().Count(property => property.NameEquals(nameof(ExecutionSteps))) > 1)
            throw new JsonException("Duplicate workflow execution counter.");
        return JsonSerializer.Deserialize(json, AgentWorkflowJsonContext.Default.AgentWorkflowState);
    }

    /// <summary>创建初始状态。</summary>
    /// <param name="sessionId">新运行关联的会话标识。</param>
    /// <returns>尚未尝试节点、累计步数为零的新状态；不能用于替代旧检查点。</returns>
    public static AgentWorkflowState Create(Guid sessionId) => new() { SessionId = sessionId, ExecutionSteps = 0 };
}

/// <summary>工作流内重命名会话工具参数；字段名与 <c>ai.chat.sessions.rename</c> 契约一致。</summary>
internal sealed record WorkflowRenameSessionArguments(
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("title")] string Title);

[JsonSerializable(typeof(AgentWorkflowState))]
[JsonSerializable(typeof(WorkflowRenameSessionArguments))]
[JsonSerializable(typeof(AgentValidationResult))]
internal sealed partial class AgentWorkflowJsonContext : JsonSerializerContext;
