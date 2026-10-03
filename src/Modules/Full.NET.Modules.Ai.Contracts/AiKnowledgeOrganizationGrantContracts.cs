using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>只替换机构授予，保留明确用户名单与角色授予；空集合撤销全部机构授予。</summary>
/// <param name="OrganizationUnitIds">当前可信范围的活动机构，最多 100 个唯一非空标识。</param>
/// <param name="Version">对应知识库或文档的并发版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAiKnowledgeOrganizationGrantsRequest(IReadOnlyList<Guid> OrganizationUnitIds, int Version);

/// <summary>所有者可见的机构授予；保留已禁用机构标识供明确撤销，不表示当前读取资格。</summary>
/// <param name="ResourceId">知识库或文档标识。</param>
/// <param name="OrganizationUnitIds">按标识稳定排序的机构授予。</param>
/// <param name="Version">当前资源并发版本。</param>
public sealed record AiKnowledgeOrganizationGrantsResponse(Guid ResourceId, IReadOnlyList<Guid> OrganizationUnitIds, int Version);
