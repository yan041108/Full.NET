using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>FAQ 草稿正文；有效期只约束未来发布，不把保存草稿解释为已生效。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqContent(string Question, IReadOnlyList<string> Aliases, string Answer, string Category,
    DateTimeOffset? ValidFromUtc, DateTimeOffset? ExpiresAtUtc);
/// <summary>完整已发布文档选择；服务器重新读取文件、回执和清单，不接受正文或客户端派生类型。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqDocumentSelection(Guid EmbeddingModelConfigId, int EmbeddingModelVersion, IReadOnlyList<AiKnowledgeSearchSource> Sources)
{
    /// <summary>显式附加本人日程期望值；完整来源始终由服务器重读，不接收正文或主体。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AiPersonalScheduleSourceSelection>? PersonalSchedules { get; init; }
}
/// <summary>独立编写必须留下独立依据，审核发布另行执行。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeFaqIndependentDraft(Guid RequestKey, AiKnowledgeFaqContent Content, string Basis);
/// <summary>文档派生类型由服务器确定且不可由后续编辑清空。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeFaqDocumentDraft(Guid RequestKey, AiKnowledgeFaqContent Content, AiKnowledgeFaqDocumentSelection Selection);
/// <summary>从含真实 FAQ 父节点的已发布选择创建派生草稿，服务器冻结全部祖先和文档叶。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeFaqDerivedDraft(Guid RequestKey, AiKnowledgeFaqContent Content, AiKnowledgeFaqDocumentSelection Selection);
/// <summary>只交付有权读取的来源身份及父边，不暴露活动租约、模型目的地或内部证明载荷。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqSourceNode(AiKnowledgeSearchSource Source, IReadOnlyList<Guid> ParentDocumentIds);
/// <summary>来源图：Roots 标识入口文档，Nodes 描述有权读取的来源节点及其父边；不暴露活动租约、模型目的地或内部证明载荷。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqSourceGraph(IReadOnlyList<Guid> Roots, IReadOnlyList<AiKnowledgeFaqSourceNode> Nodes);
/// <summary>每次修改产生不可变修订，同请求重试不能覆盖并发的新头。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveAiKnowledgeFaqDraft(Guid RequestKey, Guid ExpectedRevisionId, int ExpectedHeadVersion, AiKnowledgeFaqContent Content);
/// <summary>恢复只恢复正文，原始来源及编写依据保持不变。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreAiKnowledgeFaqDraft(Guid RequestKey, Guid ExpectedRevisionId, int ExpectedHeadVersion, Guid RevisionId);
/// <summary>复制当前草稿必须绑定原头；不得把来源改成独立类型。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CopyAiKnowledgeFaqDraft(Guid RequestKey, Guid ExpectedRevisionId, int ExpectedHeadVersion);
/// <summary>不可变修订；摘要同时绑定正文及原始来源。</summary>
public sealed record AiKnowledgeFaqDraftRevision(Guid Id, int Number, AiKnowledgeFaqContent Content, string ContentHash, DateTimeOffset CreatedAtUtc,
    Guid? RestoredFromRevisionId);
/// <summary>当前只提供草稿维护，不创建文档版本、索引、审核或发布；预留文档身份不可由普通上传端点访问。</summary>
public sealed record AiKnowledgeFaqDraftWorkspace(Guid Id, Guid KnowledgeBaseId, Guid ReservedDocumentId, string State,
    string Origin, string? Basis, string SourceHash, IReadOnlyList<AiKnowledgeSearchSource> Sources, int HeadVersion,
    AiKnowledgeFaqDraftRevision Revision)
{
    /// <summary>草稿的来源图；未携带有权来源时为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqSourceGraph? Graph { get; init; }
    /// <summary>草稿由反馈生成的准确反馈来源；普通来源草稿为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqFeedbackSource? Feedback { get; init; }
    /// <summary>草稿引用的个人日程来源选择；无相关来源时为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AiPersonalScheduleSourceSelection>? PersonalSchedules { get; init; }
}
/// <summary>重放保留原保存身份，同时返回当前头；不会让旧重试成为新的修订。</summary>
public sealed record AiKnowledgeFaqDraftMutation(AiKnowledgeFaqDraftWorkspace Workspace, Guid SavedRevisionId, bool Replayed);
/// <summary>游标指向已扫描记录，不返回不可用来源的草稿或隐藏数量。</summary>
public sealed record AiKnowledgeFaqDraftPage(IReadOnlyList<AiKnowledgeFaqDraftWorkspace> Items, Guid? NextAfterId);
/// <summary>历史正文仍经过当前完整来源核证。</summary>
public sealed record AiKnowledgeFaqDraftRevisionPage(IReadOnlyList<AiKnowledgeFaqDraftRevision> Items, Guid? NextAfterId);

/// <summary>草稿维护精确权限，Host 与 Tenant 使用相同的可信隔离边界。</summary>
public static class AiKnowledgeFaqPermissions
{
    /// <summary>读取当前作用域的 FAQ 草稿工作区与修订历史。</summary>
    public const string Read = "ai.knowledge_faqs.read";
    /// <summary>创建和修改 FAQ 草稿及其来源引用。</summary>
    public const string Write = "ai.knowledge_faqs.write";
    /// <summary>窄用途读取已批准私人回答及其派生 FAQ；不授予任意聊天会话读取权。</summary>
    public const string Feedback = "ai.knowledge_faqs.feedback";
    /// <summary>明确确认 FAQ 修订索引与可能发生的费用。</summary>
    public const string Index = "ai.knowledge_faqs.index";
    /// <summary>独立审核准确修订、完整索引代次和有效期。</summary>
    public const string Review = "ai.knowledge_faqs.review";
    /// <summary>按原申请身份发布或撤销已审核 FAQ。</summary>
    public const string Publish = "ai.knowledge_faqs.publish";
    /// <summary>导出当前可读修订及完整原始来源。</summary>
    public const string Export = "ai.knowledge_faqs.export";
    /// <summary>预览及导入保留权威来源的草稿包。</summary>
    public const string Import = "ai.knowledge_faqs.import";
}
