namespace Full.NET.Modules.Calendar.Persistence;

/// <summary>个人日程表行投影，列顺序必须与 PersonalScheduleSql 查询列表一致。</summary>
internal sealed class PersonalScheduleRecord
{
    /// <summary>逻辑主键。</summary>
    public Guid Id { get; init; }

    /// <summary>租户标识；NULL 表示 Host 级个人日程。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>所属用户标识，始终来自受信 sub 声明。</summary>
    public Guid OwnerUserId { get; init; }

    /// <summary>日程内容摘要。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>计划开始时间（UTC）。</summary>
    public DateTimeOffset StartAtUtc { get; init; }

    /// <summary>计划结束时间（UTC）。</summary>
    public DateTimeOffset EndAtUtc { get; init; }

    /// <summary>完成状态稳定机器码。</summary>
    public string Status { get; init; } = Contracts.PersonalScheduleStatuses.Pending;

    /// <summary>标记完成时间（UTC）。</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>乐观并发版本号。</summary>
    public int Version { get; init; }
}
