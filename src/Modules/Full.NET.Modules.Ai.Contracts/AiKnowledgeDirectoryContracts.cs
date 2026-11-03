using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>文件夹仅组织原知识库内内容，不改变来源权限。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveAiKnowledgeFolder(string Name, Guid? ParentId, int Version);
/// <summary>空文件夹按原版本删除，不隐式移动文档。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteAiKnowledgeFolder(int Version);
/// <summary>有界文件夹元数据，不返回无权文档数量。</summary>
public sealed record AiKnowledgeFolderResponse(Guid Id, Guid KnowledgeBaseId, Guid? ParentId, string Name, int Version);
/// <summary>组织信息使用独立版本；不把编辑标签误认为重建索引或发布。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAiKnowledgeDirectoryItem(Guid DocumentId, Guid RequestKey, int Version, Guid? FolderId,
    IReadOnlyList<string> Tags, Guid? ResponsibleUserId);
/// <summary>每项独立事务，部分失败可仅重试失败项。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAiKnowledgeDirectoryBatch(IReadOnlyList<SetAiKnowledgeDirectoryItem> Items);
/// <summary>逐项结果包含原请求身份，失败不伪装为成功或整批回滚。</summary>
public sealed record AiKnowledgeDirectoryItemResult(Guid DocumentId, Guid RequestKey, bool Succeeded, int? Version, string? ErrorCode);
/// <summary>分页投影当前可读文档、组织信息、构建候选及独立发布状态。</summary>
public sealed record AiKnowledgeDirectoryResponse(Guid Id, string Title, Guid? FolderId, IReadOnlyList<string> Tags,
    Guid? ResponsibleUserId, int Version, int DocumentVersion, string PublicationState, Guid? PublicationId, int HeadVersion,
    Guid? CandidateVersionId, Guid? PendingPublicationId);
