using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>审批 UI 展示字段；与 ArgumentsHash 并存，避免只显示哈希。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AiAgentApprovalPresentation(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("change")] string Change,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("costCeiling")] decimal? CostCeiling,
    [property: JsonPropertyName("currency")] string? Currency);
