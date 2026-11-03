namespace Full.NET.Modules.Ai.Contracts;

/// <summary>原月份、价格版本及币种分别汇总；未知或缺失金额不能合并成零。</summary>
public sealed record AiKnowledgeFinanceBucket(Guid? KnowledgeBaseId,string Attribution,string Month,
    Guid? PriceVersionId,string? Currency,long Requests,long Known,long Unknown,long Reserved,long MissingCost,
    string? RecordedCost,string? ConservativeReservation);
/// <summary>可信范围有界账本报告，没有模型用时的数据仍明确未记录。</summary>
public sealed record AiKnowledgeFinanceReport(DateTimeOffset FromUtc,DateTimeOffset ToUtc,
    string MeasurementState,IReadOnlyList<AiKnowledgeFinanceBucket> Items);
/// <summary>只含维护者库的结果分类和差评计数，不交付消息、问题或答案，缺少观测的历史保持未记录。</summary>
public sealed record AiKnowledgeQualityObservation(Guid KnowledgeBaseId,DateTimeOffset FromUtc,DateTimeOffset ToUtc,
    long RecordedAnswers,long Refused,long Measured,double? MeanPipelineMs,DateTimeOffset? FirstRecordedAtUtc,
    long CurrentNegativeFeedback,string HistoricalCoverage,string MeasurementProtocol);
