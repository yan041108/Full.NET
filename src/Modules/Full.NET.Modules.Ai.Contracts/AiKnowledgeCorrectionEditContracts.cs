using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>维护者实际读取的完整头身份；文档版本和修订头版本分别比较。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeCorrectionHeadRequest(int DocumentVersion, Guid SourceId, Guid? RevisionId,
    int HeadVersion, string SourceHash, string PayloadHash);

/// <summary>单次有界转换；replace、split、confirm、append、merge、exclude 各自只接受必需字段。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeCorrectionEditOperation(string Kind, Guid BlockId, Guid? PartId, string? Text, int? Offset, bool? Excluded);

/// <summary>一次人工操作写一份不可变修订；请求键在响应丢失后保持不变，不接收客户端原文锚点。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeCorrectionEditRequest(Guid RequestKey, AiKnowledgeCorrectionHeadRequest Expected,
    string Reason, AiKnowledgeCorrectionEditOperation Operation);

/// <summary>恢复历史或采用独立重解析也写新修订，必须明确选择原来源及可空的历史修订。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeCorrectionRestoreRequest(Guid RequestKey, AiKnowledgeCorrectionHeadRequest Expected,
    string Reason, Guid TargetSourceId, Guid? TargetRevisionId);

/// <summary>不可变提交回执及历史摘要，不包含正文或原会话；恢复不会使旧身份成为新头。</summary>
public sealed record AiKnowledgeCorrectionRevisionResponse(Guid Id, Guid VersionId, Guid SourceId, Guid? ParentId,
    int HeadVersion, int DocumentVersion, string PayloadHash, string Reason, DateTimeOffset CreatedAtUtc);

/// <summary>按修订头版本倒序、每页最多二十项；游标是下页独占上界。</summary>
public sealed record AiKnowledgeCorrectionRevisionPageResponse(IReadOnlyList<AiKnowledgeCorrectionRevisionResponse> Items, int? NextHeadVersion);
