namespace Full.NET.Modules.Ai.Contracts;

/// <summary>可靠确认的最新来源锚点，不表示待执行、付费资格或完整索引就绪。</summary>
/// <param name="DocumentId">当前启用知识库内的文档。</param>
/// <param name="Title">当前文档标题。</param>
/// <param name="VersionId">最新已完成导入的来源版本。</param>
/// <param name="VersionNumber">来源版本的单调序号。</param>
/// <param name="CreatedAtUtc">首次记录增量意图的 UTC 时间。</param>
/// <param name="UpdatedAtUtc">最近推进来源意图的 UTC 时间。</param>
public sealed record AiKnowledgeIndexChangeItem(Guid DocumentId, string Title, Guid VersionId, int VersionNumber,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>当前所有者的一页来源锚点；可变目录不承诺跨页一致快照。</summary>
/// <param name="Items">本页至多五十条已确认来源。</param>
/// <param name="NextDocumentId">下一页的数据库文档游标，空值表示当前扫描结束。</param>
public sealed record AiKnowledgeIndexChangePage(IReadOnlyList<AiKnowledgeIndexChangeItem> Items, Guid? NextDocumentId);
