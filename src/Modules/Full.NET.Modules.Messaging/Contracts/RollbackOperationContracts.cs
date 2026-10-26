using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging.Contracts;

/// <summary>恢复原 CDC 链路的人工理由；不能指定所有者或控制面地址。</summary>
public sealed record RecoverRollbackRequest(string Reason);

/// <summary>持久回退状态，不返回内部资源快照。</summary>
public sealed record RollbackOperationResponse(Guid Generation, string EventType, int SchemaVersion,
    EventDeliveryRollbackPhase Phase, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
