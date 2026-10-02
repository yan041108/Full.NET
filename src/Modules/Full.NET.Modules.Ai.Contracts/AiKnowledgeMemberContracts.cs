using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>整量替换明确成员；所有者隐式持有，空集合立即撤销全部明确成员。</summary>
/// <param name="UserIds">当前可信范围的活动用户标识，最多 100 个；不能包含所有者。</param>
/// <param name="Version">知识库并发版本；成员、元数据和审批共享同一保护。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAiKnowledgeMembersRequest(IReadOnlyList<Guid> UserIds, int Version);

/// <summary>仅供所有者查看的明确成员及最新知识库版本。</summary>
/// <param name="KnowledgeBaseId">知识库标识。</param>
/// <param name="UserIds">稳定排序后的明确成员标识，不包含隐式所有者。</param>
/// <param name="Version">知识库并发版本。</param>
public sealed record AiKnowledgeMembersResponse(Guid KnowledgeBaseId, IReadOnlyList<Guid> UserIds, int Version);
