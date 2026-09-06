namespace Full.NET.Modules.Calendar.Contracts;

/// <summary>
/// 个人日程相关操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class CalendarPermissions
{
    /// <summary>允许读取当前用户的个人日程列表与详情。</summary>
    public const string Read = "calendar.personal_schedules.read";

    /// <summary>允许创建当前用户的个人日程。</summary>
    public const string Create = "calendar.personal_schedules.create";

    /// <summary>允许更新当前用户的个人日程。</summary>
    public const string Update = "calendar.personal_schedules.update";

    /// <summary>允许删除当前用户的个人日程。</summary>
    public const string Delete = "calendar.personal_schedules.delete";

    /// <summary>允许切换当前用户个人日程的完成状态。</summary>
    public const string SetStatus = "calendar.personal_schedules.set_status";
}
