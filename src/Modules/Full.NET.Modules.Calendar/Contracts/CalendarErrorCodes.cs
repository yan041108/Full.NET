namespace Full.NET.Modules.Calendar.Contracts;

/// <summary>
/// Calendar 模块稳定错误码集合，作为机器契约不可本地化。
/// </summary>
public static class CalendarErrorCodes
{
    /// <summary>Calendar 错误码前缀。</summary>
    public const string Prefix = "calendar.";

    /// <summary>个人日程未找到或不属于当前用户。</summary>
    public const string PersonalScheduleNotFound = "calendar.personal_schedule_not_found";

    /// <summary>个人日程起止时间无效，结束时间必须不早于开始时间。</summary>
    public const string PersonalScheduleInvalidTimeRange =
        "calendar.personal_schedule_invalid_time_range";

    /// <summary>个人日程状态无效，只允许 pending 或 completed。</summary>
    public const string PersonalScheduleInvalidStatus =
        "calendar.personal_schedule_invalid_status";

    /// <summary>个人日程乐观版本号不符，并发更新冲突。</summary>
    public const string PersonalScheduleConcurrencyConflict =
        "calendar.personal_schedule_concurrency_conflict";

    /// <summary>个人日程内容长度或格式校验失败。</summary>
    public const string PersonalScheduleInvalidContent =
        "calendar.personal_schedule_invalid_content";
}
