using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>只审核当前完整候选，不接受客户端声明来源或已批准标记。</summary>
/// <param name="RequestKey">同一规范请求的重试 UUID。</param>
/// <param name="VersionId">当前候选文件版本。</param>
/// <param name="ExpectedHeadVersion">读取时的发布头版本，首次为零。</param>
/// <param name="EffectiveFromUtc">正式内容生效起点。</param>
/// <param name="EffectiveToUtc">正式内容失效终点，不能无限期发布。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestAiKnowledgePublication(Guid RequestKey, Guid VersionId, int ExpectedHeadVersion,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc);
/// <summary>独立审核人明确确认冻结提案，决定不能代替发布操作。</summary>
/// <param name="Approve">明确批准或拒绝。</param>
/// <param name="Version">当前提案版本。</param>
/// <param name="SnapshotHash">界面已展示的冻结摘要。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewAiKnowledgePublication(bool Approve, int Version, string SnapshotHash);
/// <summary>原维护会话确认切换已批准的原候选。</summary>
/// <param name="Version">当前提案版本。</param>
/// <param name="SnapshotHash">已批准提案摘要。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublishAiKnowledgePublication(int Version, string SnapshotHash);
/// <summary>下架不回退旧版本，也不删除证据与已有业务数据。</summary>
/// <param name="ExpectedHeadVersion">原发布头并发版本。</param>
/// <param name="PublicationId">明确被撤回的发布身份。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawAiKnowledgePublication(int ExpectedHeadVersion, Guid PublicationId);

/// <summary>无正文、凭据或回执载荷的人工确认投影；索引状态独立于发布状态。</summary>
public sealed record AiKnowledgePublicationResponse(Guid Id, Guid DocumentId, Guid VersionId, Guid GenerationId,
    string StateKey, string SnapshotHash, int PolicyVersion, int ExpectedHeadVersion, int Version,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc, Guid ActorUserId, Guid? ReviewerUserId,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ReviewedAtUtc);
/// <summary>永久发布头；空发布身份表示下架，不隐式选择旧版。</summary>
public sealed record AiKnowledgePublicationHeadResponse(Guid DocumentId, Guid? PublicationId, int Version);

/// <summary>发布治理采用独立精确权限；文件夹管理不能赋予来源读取或发布资格。</summary>
public static class AiKnowledgePublicationPermissions
{
    public const string Read = "ai.knowledge_publications.read";
    public const string Request = "ai.knowledge_publications.request";
    public const string Review = "ai.knowledge_publications.review";
    public const string Publish = "ai.knowledge_publications.publish";
    public const string Withdraw = "ai.knowledge_publications.withdraw";
    public const string DirectoryManage = "ai.knowledge_directories.manage";
}
