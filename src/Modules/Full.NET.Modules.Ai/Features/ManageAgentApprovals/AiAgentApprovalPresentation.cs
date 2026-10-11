using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>审批展示与目标版本快照；历史无版本记录可读，但不能作为执行凭证。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AiAgentApprovalPresentation(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("change")] string Change,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("costCeiling")] decimal? CostCeiling,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("targetVersion")] int? TargetVersion = null);
