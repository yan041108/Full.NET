namespace Full.NET.Modules.Ai.Contracts;

/// <summary>所有者可选择的当前可信租户活动机构，不表示已经授予资源访问。</summary>
/// <param name="Id">机构权威标识。</param>
/// <param name="Code">稳定机器编码。</param>
/// <param name="Name">显示名称。</param>
public sealed record AiKnowledgeOrganizationCandidateResponse(Guid Id, string Code, string Name);
