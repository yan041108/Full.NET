using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>每次最多恢复五十条来源；空游标从头幂等重扫，不代表收费执行授权。</summary>
/// <param name="AfterDocumentId">上页返回的数据库内稳定文档游标。</param>
/// <param name="PageSize">本次最多处理的来源数量。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeIncrementalRecoveryRequest(Guid? AfterDocumentId = null, int PageSize = 50);

/// <summary>仅报告本页意图写入，不能解释为索引创建、模型处理或检索就绪。</summary>
/// <param name="Scanned">本页实际复核的来源数量。</param>
/// <param name="Updated">本页实际新增或推进的意图数量。</param>
/// <param name="NextDocumentId">有下一页时使用的文档游标，否则为空。</param>
public sealed record AiKnowledgeIncrementalRecoveryResponse(int Scanned, int Updated, Guid? NextDocumentId);
