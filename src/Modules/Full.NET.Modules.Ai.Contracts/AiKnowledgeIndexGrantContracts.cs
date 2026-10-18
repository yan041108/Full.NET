using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>显式授权原登录下的未来增量索引；不改变知识库模型和数据分类批准，不授权未知收费重发。</summary>
/// <param name="ClientRequestId">本人在知识库内的幂等键。</param>
/// <param name="KnowledgeBaseVersion">确认时的知识库及模型批准修订。</param>
/// <param name="ExpiresAtUtc">绝对到期时间，创建时须在一分钟至二十四小时内。</param>
/// <param name="DocumentLimit">最多创建的文档版本任务数，范围一至六十四；失败不返还次数。</param>
/// <param name="InputTokenLimitPerDocument">每个任务的累计输入预算预留上限，范围一至一百万；不是金额硬限。</param>
/// <param name="ConfirmPaidDispatch">明确确认可产生模型费用，默认不授权。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeIndexGrantRequest(Guid ClientRequestId, int KnowledgeBaseVersion, DateTimeOffset ExpiresAtUtc,
    int DocumentLimit, long InputTokenLimitPerDocument, bool ConfirmPaidDispatch);

/// <summary>原所有者撤销授权，不能撤销已完成外部动作或释放未知收费。</summary>
/// <param name="Version">最新授权修订，防止误用过期状态。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RevokeAiKnowledgeIndexGrantRequest(int Version);

/// <summary>有界授权状态，不包含原会话、安全戳或模型凭据。</summary>
/// <param name="Id">授权标识。</param>
/// <param name="KnowledgeBaseId">所属知识库。</param>
/// <param name="KnowledgeBaseVersion">冻结的知识库及模型批准修订。</param>
/// <param name="ExpiresAtUtc">绝对到期时间。</param>
/// <param name="DocumentLimit">总任务准入上限。</param>
/// <param name="AdmittedDocuments">已消耗的准入次数，不因失败或撤销返还。</param>
/// <param name="InputTokenLimitPerDocument">每任务累计输入预留上限。</param>
/// <param name="IsRevoked">是否已明确撤销；到期、权限和来源失效另行实时拒绝。</param>
/// <param name="Version">当前并发修订。</param>
public sealed record AiKnowledgeIndexGrantResponse(Guid Id, Guid KnowledgeBaseId, int KnowledgeBaseVersion, DateTimeOffset ExpiresAtUtc,
    int DocumentLimit, int AdmittedDocuments, long InputTokenLimitPerDocument, bool IsRevoked, int Version);

/// <summary>增量收费委托独立于手动索引；读取、授权和撤销分别控制。</summary>
public static class AiKnowledgeIndexGrantPermissions
{
    /// <summary>读取本人知识库的收费增量授权。</summary>
    public const string Read = "ai.knowledge_documents.index_grant_read";
    /// <summary>明确授权原会话后台增量收费派发。</summary>
    public const string Create = "ai.knowledge_documents.index_grant_create";
    /// <summary>撤销本人既有增量收费授权。</summary>
    public const string Revoke = "ai.knowledge_documents.index_grant_revoke";
}
