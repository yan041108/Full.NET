namespace Full.NET.Agents.Runtime;

/// <summary>幂等创建请求；主体与绑定摘要由可信 API 写入，模型参数不能覆盖。</summary>
public sealed record AgentRunDraft(
    Guid ClientRequestId,
    string RequestHash,
    string ScopeKey,
    Guid? TenantId,
    Guid ActorUserId,
    Guid SessionId,
    Guid AuthorizationBindingId,
    string SecurityStamp,
    string ActorScope,
    string EffectiveScope,
    string DefinitionKey,
    int DefinitionVersion,
    string BudgetJson,
    DateTimeOffset DeadlineAtUtc,
    Guid? PredeterminedRunId = null);
