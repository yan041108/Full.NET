using Full.NET.Abstractions.Results;

namespace Full.NET.Data.Abstractions;

/// <summary>Legacy 死信的 Host 运维端口；写操作必须由调用方放在所有权门和审计事务内。</summary>
public interface IOutboxDeadLetterStore
{
    /// <summary>有界分页读取死信，不返回载荷。</summary>
    Task<PagedResult<OutboxDeadLetter>> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken);

    /// <summary>按精确标识读取状态；已处理或已回队时返回空。</summary>
    Task<OutboxDeadLetter?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>条件回队，只重置死信与调度字段，保留载荷和历史尝试次数。</summary>
    Task<bool> TryRequeueAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>死信查询边界；只允许精确过滤和有界时间窗口。</summary>
public sealed record DeadLetterFilter(int Page = 1, int PageSize = 20, string? MessageType = null,
    int? SchemaVersion = null, string? ErrorCode = null, DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null, string? ConsumerName = null);

/// <summary>Legacy 死信元数据；不把业务载荷暴露给管理列表。</summary>
public sealed record OutboxDeadLetter(Guid Id, string MessageType, int SchemaVersion, Guid? TenantId,
    int Attempts, string? ErrorCode, DateTimeOffset OccurredAtUtc, DateTimeOffset FailedAtUtc);
