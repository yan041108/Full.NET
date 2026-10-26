using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Messaging.Contracts;

/// <summary>两个死信来源共享的只读元数据；不向客户端返回原始载荷或异常文本。</summary>
public sealed record OperationalDeadLetterResponse(Guid Id, string Source, string? ConsumerName,
    Guid? MessageId, string? MessageType, int? SchemaVersion, Guid? TenantId, int Attempts,
    string? ErrorCode, DateTimeOffset FirstFailedAtUtc, DateTimeOffset LastFailedAtUtc,
    string? SourceTopic, int? SourcePartition, string? SourceOffset, string? DlqTopic,
    int? DlqPartition, string? DlqOffset, bool CanReplay, DateTimeOffset? ReplayedAtUtc);

/// <summary>恢复请求只提供运维理由；消息来源和操作者由受信任服务端状态决定。</summary>
public sealed record RecoverDeadLetterRequest(string Reason);

/// <summary>单条恢复结果，恢复完成不等于 Legacy 业务处理已完成。</summary>
public sealed record RecoverDeadLetterResponse(Guid Id, string Outcome);
