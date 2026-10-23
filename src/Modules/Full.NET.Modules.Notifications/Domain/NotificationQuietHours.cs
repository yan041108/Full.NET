using System.Text.Json;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>
/// 静默时段求值；按偏好声明的时区在本地墙钟时间判断是否处于静默，
/// 并把“下一可发送时间”换算回 UTC。
/// </summary>
/// <remarks>
/// 使用 <see cref="TimeZoneInfo"/> 规则而不是当前偏移进行换算，避免跨夏令时切换边界时算错：
/// 前跳造成的“不存在时间”顺延至首个有效分钟，回拨造成的“重复时间”取较晚的 UTC 时刻，
/// 避免回拨后再次进入静默区间。
/// JSON 结构：<c>{ "timeZone": "Asia/Shanghai", "start": "22:00", "end": "08:00" }</c>，
/// <c>start &gt; end</c> 表示跨午夜窗口。解析失败一律视为不静默，交由上游失败关闭处理。
/// </remarks>
internal static class NotificationQuietHours
{
    /// <summary>判断给定 UTC 时刻在其声明时区内是否落在静默区间内。</summary>
    /// <param name="quietHoursJson">偏好中的静默时段声明。</param>
    /// <param name="now">当前 UTC 时刻。</param>
    /// <returns>处于静默区间返回 true；解析失败或不在区间返回 false。</returns>
    public static bool IsInQuietHours(string quietHoursJson, DateTimeOffset now)
        => TryParse(quietHoursJson, out var timeZone, out var start, out var end)
           && IsWithin(TimeZoneInfo.ConvertTime(now, timeZone).TimeOfDay, start, end);

    /// <summary>计算下一可发送的 UTC 时刻；静默结束时间已过则顺延到次日。</summary>
    /// <param name="quietHoursJson">偏好中的静默时段声明。</param>
    /// <param name="now">当前 UTC 时刻。</param>
    /// <returns>下一可发送的 UTC 时刻；声明无效时返回 null，由调用方决定回退策略。</returns>
    public static DateTimeOffset? NextAllowedAt(string quietHoursJson, DateTimeOffset now)
    {
        if (!TryParse(quietHoursJson, out var timeZone, out _, out var end))
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        if (!IsInQuietHours(quietHoursJson, now))
        {
            return now;
        }

        var candidateLocal = localNow.Date + end;
        if (candidateLocal <= localNow.DateTime)
        {
            candidateLocal = candidateLocal.AddDays(1);
        }

        return ToUtc(candidateLocal, timeZone);
    }

    private static bool IsWithin(TimeSpan local, TimeSpan start, TimeSpan end)
        => start <= end
            ? local >= start && local < end
            : local >= start || local < end;

    /// <summary>把本地墙钟时间按目标时区规则换算为 UTC，处理夏令时前跳的不存在时间。</summary>
    private static DateTimeOffset ToUtc(DateTime localWallClock, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        // 时区前跳不一定是一小时，不能硬编码 AddHours(1)。
        while (timeZone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(1);
        }

        var offset = timeZone.IsAmbiguousTime(unspecified)
            ? timeZone.GetAmbiguousTimeOffsets(unspecified).Min()
            : timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private static bool TryParse(
        string quietHoursJson,
        out TimeZoneInfo timeZone,
        out TimeSpan start,
        out TimeSpan end)
    {
        timeZone = TimeZoneInfo.Utc;
        start = TimeSpan.Zero;
        end = TimeSpan.Zero;
        try
        {
            using var document = JsonDocument.Parse(quietHoursJson);
            var root = document.RootElement;
            var timeZoneId = root.GetProperty("timeZone").GetString();
            timeZone = string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            start = TimeSpan.ParseExact(root.GetProperty("start").GetString() ?? "00:00", "hh\\:mm", null);
            end = TimeSpan.ParseExact(root.GetProperty("end").GetString() ?? "00:00", "hh\\:mm", null);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
