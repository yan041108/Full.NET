namespace Full.NET.Modules.Jobs.Contracts;

/// <summary>Host 任务计划批量状态变更的有界上限。</summary>
public static class HostJobScheduleBatchLimits
{
    /// <summary>单次批量暂停或恢复允许的最大计划数。</summary>
    public const int MaxStateChangeCount = 50;
}

/// <summary>批量暂停/恢复单条计划请求项；携带乐观并发版本。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="ScheduleId">计划标识。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record BatchChangeHostJobScheduleStateItem(
    Guid ScheduleId,
    int Version);

/// <summary>批量暂停或恢复任务计划请求。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Items">待变更的计划集合；顺序决定结果回显顺序。</param>
public sealed record BatchChangeHostJobScheduleStateRequest(
    IReadOnlyList<BatchChangeHostJobScheduleStateItem> Items);

/// <summary>批量状态变更单条结果。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="ScheduleId">本条结果对应的计划标识。</param>
/// <param name="Succeeded">本条是否变更成功。</param>
/// <param name="Schedule">成功时返回最新计划投影；失败时为 <see langword="null"/>。</param>
/// <param name="ErrorCode">失败时返回稳定错误码；成功时为 <see langword="null"/>。</param>
/// <param name="Message">失败时的可读说明；成功时为 <see langword="null"/>。</param>
public sealed record BatchChangeHostJobScheduleStateResultItem(
    Guid ScheduleId,
    bool Succeeded,
    HostJobScheduleResponse? Schedule,
    string? ErrorCode,
    string? Message);

/// <summary>批量状态变更汇总。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="SucceededCount">实际变更成功的计划数。</param>
/// <param name="Results">逐条结果；顺序与请求集合一致。</param>
public sealed record BatchChangeHostJobScheduleStateResponse(
    int SucceededCount,
    IReadOnlyList<BatchChangeHostJobScheduleStateResultItem> Results);
