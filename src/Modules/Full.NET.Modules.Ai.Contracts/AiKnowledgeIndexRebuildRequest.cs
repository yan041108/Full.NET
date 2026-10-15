using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>显式确认新增费用并绑定对账快照；租户、模型和所有者由服务器推导。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeIndexRebuildRequest(Guid ClientRequestId, Guid ExpectedTaskId,
    int ExpectedDocumentVersion, int ExpectedKnowledgeBaseVersion, bool ConfirmAdditionalCost);
