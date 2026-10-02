namespace Full.NET.Agents.Runtime;

/// <summary>幂等创建请求；主体与绑定摘要由可信 API 写入，模型参数不能覆盖。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ClientRequestId">客户端幂等键；同一键重复提交返回已有运行，不重复创建。</param>
/// <param name="RequestHash">请求主体摘要；用于检测同幂等键下的内容篡改。</param>
/// <param name="ScopeKey">作用域键；标识本次运行所属的业务作用域。</param>
/// <param name="TenantId">租户标识；Host 级调用为 <see langword="null"/>。</param>
/// <param name="ActorUserId">发起用户标识；用于审计与权限校验。</param>
/// <param name="SessionId">会话标识；用于关联同一会话内的多次运行。</param>
/// <param name="AuthorizationBindingId">授权绑定标识；指向已校验的授权记录。</param>
/// <param name="SecurityStamp">授权安全戳；授权变更后失效，需重新授权。</param>
/// <param name="ActorScope">用户原始作用域；由可信 API 写入，不可被模型覆盖。</param>
/// <param name="EffectiveScope">本次运行生效作用域；可能因授权裁剪而小于 ActorScope。</param>
/// <param name="DefinitionKey">代理定义键。</param>
/// <param name="DefinitionVersion">代理定义版本。</param>
/// <param name="BudgetJson">预算配置 JSON 字符串；由调用方按定义序列化为不可变快照。</param>
/// <param name="DeadlineAtUtc">运行截止时间（UTC）；超期后运行将被中止。</param>
/// <param name="PredeterminedRunId">预分配运行标识；调用方需保证全局唯一，<see langword="null"/> 时由服务端生成。</param>
/// <param name="SessionKind">会话类型；默认为 refresh，用于区分会话生命周期策略。</param>
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
    Guid? PredeterminedRunId = null,
    string SessionKind = "refresh");
