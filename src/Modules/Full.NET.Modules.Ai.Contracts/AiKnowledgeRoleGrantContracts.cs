using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>只替换角色授予，保留明确用户名单；空集合撤销全部角色授予。</summary>
/// <param name="RoleIds">当前可信范围的活动普通角色，最多 100 个唯一非空标识。</param>
/// <param name="Version">对应知识库或文档的并发版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAiKnowledgeRoleGrantsRequest(IReadOnlyList<Guid> RoleIds, int Version);

/// <summary>所有者可见的角色授予；保留已禁用角色标识供明确撤销，不表示当前读取资格。</summary>
/// <param name="ResourceId">知识库或文档标识。</param>
/// <param name="RoleIds">按标识稳定排序的角色授予。</param>
/// <param name="Version">当前资源并发版本。</param>
public sealed record AiKnowledgeRoleGrantsResponse(Guid ResourceId, IReadOnlyList<Guid> RoleIds, int Version);
