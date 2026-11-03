using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>仅准备当前文件的本地解析，不创建索引、收费模型操作或发布。</summary>
/// <param name="RequestKey">客户端重试身份，响应丢失时保持不变。</param>
/// <param name="DocumentVersion">期望的文档维护版本，不能用文件版本号替代。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeCorrectionPrepareRequest(Guid RequestKey, int DocumentVersion);

/// <summary>持久解析准备状态；完成只表示工作台来源可读，不表示已索引或已发布。</summary>
/// <param name="Id">服务端准备任务 UUID v7。</param>
/// <param name="VersionId">被冻结的文件版本。</param>
/// <param name="DocumentVersion">被请求的文档维护版本。</param>
/// <param name="State">queued、running、completed、failed 或 cancelled。</param>
/// <param name="AttemptCount">已领取次数，最多三次。</param>
/// <param name="SourceId">完成后才存在的原解析来源标识。</param>
/// <param name="FailureCode">受控失败机器码，不含外部异常正文。</param>
/// <param name="CreatedAtUtc">请求建立时间。</param>
/// <param name="UpdatedAtUtc">最近状态变化时间。</param>
public sealed record AiKnowledgeCorrectionPreparationResponse(Guid Id, Guid VersionId, int DocumentVersion, string State,
    int AttemptCount, Guid? SourceId, string? FailureCode, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>工作台一次只读取一块及其完整原始上下文，避免把整份大文档装入页面。</summary>
public sealed record AiKnowledgeCorrectionWorkspaceResponse(Guid VersionId, int DocumentVersion, Guid SourceId, Guid? RevisionId,
    int HeadVersion, string SourceHash, string PayloadHash, string ParserRuntimeId, string ContentType, string OriginalFileName,
    int BlockIndex, int TotalBlocks, bool IsSourceView, AiKnowledgeCorrectionBlockResponse Block);

/// <summary>人工块保留排除状态、排版及所有片段；排除不删除原解析。</summary>
public sealed record AiKnowledgeCorrectionBlockResponse(Guid Id, bool Excluded, string Separator, IReadOnlyList<AiKnowledgeCorrectionPartResponse> Parts);

/// <summary>维护预览中的原文、更正或补写；原识别置信度按实际值显示，不授予索引资格。</summary>
public sealed record AiKnowledgeCorrectionPartResponse(Guid Id, string Text, string Prefix, string ContentKind, Guid? AnchorId,
    string? OriginalText, int? SourceOffset, int? SourceLength, AiKnowledgeSourceLocationResponse? SourceLocation);
