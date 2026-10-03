using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建只有元数据的草稿；正文、文件标识及索引状态不属于本请求。</summary>
/// <param name="Title">标题，最多 200 个字符。</param>
/// <param name="Description">可选描述，最多 2000 个字符。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeDocumentRequest(string Title, string? Description);

/// <summary>所有者编辑文档元数据；不会创建文件版本或推进索引。</summary>
/// <param name="Title">有界标题。</param>
/// <param name="Description">有界描述。</param>
/// <param name="Version">读取时的文档乐观并发版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAiKnowledgeDocumentRequest(string Title, string? Description, int Version);

/// <summary>软删除先提交权威拒读状态；重复或跨范围访问不暴露已删除记录。</summary>
/// <param name="Version">读取时的文档并发版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteAiKnowledgeDocumentRequest(int Version);

/// <summary>文档独立明确用户授权，只允许当前知识库成员；空名单立即撤销。</summary>
/// <param name="UserIds">最多 100 个唯一非空用户标识，不包括所有者。</param>
/// <param name="Version">文档元数据与授权共享的并发版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAiKnowledgeDocumentMembersRequest(IReadOnlyList<Guid> UserIds, int Version);

/// <summary>只有已授权主体可读取的文档标题与描述；草稿不包含内容或文件引用。</summary>
/// <param name="Id">应用生成的 UUID v7 文档标识。</param>
/// <param name="KnowledgeBaseId">所属知识库。</param>
/// <param name="Title">受授权保护的标题。</param>
/// <param name="Description">受授权保护的描述。</param>
/// <param name="Status">固定 draft；不表示内容已上传或索引已就绪。</param>
/// <param name="CreatedAtUtc">创建时间 UTC。</param>
/// <param name="UpdatedAtUtc">最近元数据或授权变更时间 UTC。</param>
/// <param name="Version">文档并发版本。</param>
public sealed record AiKnowledgeDocumentResponse(Guid Id, Guid KnowledgeBaseId, string Title, string? Description,
    string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, int Version);

/// <summary>只对知识库所有者公开的文档授权名单。</summary>
/// <param name="DocumentId">文档标识。</param>
/// <param name="UserIds">独立文档授权用户，读取时仍核对知识库成员资格。</param>
/// <param name="Version">文档并发版本。</param>
public sealed record AiKnowledgeDocumentMembersResponse(Guid DocumentId, IReadOnlyList<Guid> UserIds, int Version);
