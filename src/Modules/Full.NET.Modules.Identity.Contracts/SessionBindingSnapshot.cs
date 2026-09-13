namespace Full.NET.Modules.Identity.Contracts;

/// <summary>创建持久运行时冻结的会话绑定；Worker 派发前重验，不接受模型或客户端覆盖。</summary>
public sealed record SessionBindingSnapshot(
    Guid UserId,
    Guid? TenantId,
    Guid SessionId,
    string SecurityStamp,
    string ActorScope,
    string EffectiveScope);
