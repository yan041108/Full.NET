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

    /// <summary>序列化为检查点 JSON。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, AgentWorkflowJsonContext.Default.AgentWorkflowState);

    /// <summary>从检查点 JSON 反序列化。</summary>
    public static AgentWorkflowState? FromJson(string json) =>
        JsonSerializer.Deserialize(json, AgentWorkflowJsonContext.Default.AgentWorkflowState);

    /// <summary>创建初始状态。</summary>
    public static AgentWorkflowState Create(Guid sessionId) => new() { SessionId = sessionId };
}

[JsonSerializable(typeof(AgentWorkflowState))]
internal sealed partial class AgentWorkflowJsonContext : JsonSerializerContext;
