using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>工作流运行会话快照；持久化到 Run 会话字段。</summary>
internal sealed record AiWorkflowSessionSnapshot(
    [property: JsonPropertyName("workflow")] string Workflow,
    [property: JsonPropertyName("outputs")] Dictionary<string, string> Outputs,
    [property: JsonPropertyName("finalText")] string? FinalText);