namespace Full.NET.Modules.Calendar.Contracts;

/// <summary>
/// 个人日程状态机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
public static class PersonalScheduleStatuses
{
    /// <summary>待完成状态。</summary>
    public const string Pending = "pending";

    /// <summary>已完成状态。</summary>
    public const string Completed = "completed";
}

/// <summary>个人日程响应契约，面向当前用户列表与详情接口。</summary>
/// <param name="Id">个人日程标识。</param>
/// <param name="Content">日程内容摘要。</param>
/// <param name="StartAtUtc">计划开始时间（UTC）。</param>
/// <param name="EndAtUtc">计划结束时间（UTC）。</param>
/// <param name="Status">完成状态稳定机器码，取值自 PersonalScheduleStatuses。</param>
/// <param name="CompletedAtUtc">标记完成时间（UTC），未完成时为 null。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record PersonalScheduleResponse(
    Guid Id,
    string Content,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建个人日程请求契约。</summary>
/// <param name="Content">日程内容，最长 256 字符。</param>
/// <param name="StartAtUtc">计划开始时间（UTC）。</param>
/// <param name="EndAtUtc">计划结束时间（UTC）。</param>
public sealed record CreatePersonalScheduleRequest(
    string Content,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc);

/// <summary>更新个人日程请求契约。</summary>
/// <param name="Content">日程内容，最长 256 字符。</param>
/// <param name="StartAtUtc">计划开始时间（UTC）。</param>
/// <param name="EndAtUtc">计划结束时间（UTC）。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdatePersonalScheduleRequest(
    string Content,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    int Version);

/// <summary>带版本号的个人日程变更请求，用于删除与状态切换。</summary>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record ChangePersonalScheduleRequest(int Version);

/// <summary>切换个人日程完成状态请求契约。</summary>
/// <param name="Status">目标状态稳定机器码，取值自 PersonalScheduleStatuses。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record SetPersonalScheduleStatusRequest(
    string Status,
    int Version);
