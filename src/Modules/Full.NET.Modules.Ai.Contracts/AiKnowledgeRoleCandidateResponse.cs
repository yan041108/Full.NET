namespace Full.NET.Modules.Ai.Contracts;

/// <summary>知识库所有者可选择的当前作用域活动普通角色，不表示已经授予访问权。</summary>
/// <param name="Id">权威角色标识。</param>
/// <param name="Code">稳定机器编码。</param>
/// <param name="Name">显示名称。</param>
public sealed record AiKnowledgeRoleCandidateResponse(Guid Id, string Code, string Name);
