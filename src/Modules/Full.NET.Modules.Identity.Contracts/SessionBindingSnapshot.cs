namespace Full.NET.Modules.Identity.Contracts;

/// <summary>创建持久运行时冻结的会话绑定；Worker 派发前重验，不接受模型或客户端覆盖。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UserId">绑定归属的用户标识。</param>
/// <param name="TenantId">绑定归属的租户标识；跨租户或无租户场景为 <see langword="null"/>。</param>
/// <param name="SessionId">会话标识。</param>
/// <param name="SecurityStamp">安全戳；变更后旧会话绑定立即失效。</param>
/// <param name="ActorScope">请求方声明的作用域。</param>
/// <param name="EffectiveScope">经授权策略裁剪后的实际生效作用域。</param>
/// <param name="SessionKind">会话类型，默认 Refresh；取值见 SessionBindingKinds。</param>
public sealed record SessionBindingSnapshot(
    Guid UserId,
    Guid? TenantId,
    Guid SessionId,
    string SecurityStamp,
    string ActorScope,
    string EffectiveScope,
    string SessionKind = SessionBindingKinds.Refresh);
