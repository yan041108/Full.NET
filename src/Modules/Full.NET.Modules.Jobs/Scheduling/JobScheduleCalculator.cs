using Cronos;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.Modules.Jobs.Scheduling;

internal sealed record JobScheduleDueDecision(
    bool CreateExecution,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? NextExecutionAtUtc,
    DateTimeOffset? CompletedAtUtc);

internal static class JobScheduleCalculator
{
    public static string NormalizeTimeZoneId(string timeZoneId)
    {
        var candidate = timeZoneId?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            throw new TimeZoneNotFoundException(
                "The time-zone identifier is required.");
        }

        if (string.Equals(candidate, "UTC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(
                candidate,
                out var ianaId)
            && !string.IsNullOrWhiteSpace(ianaId))
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            return ianaId;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(candidate);
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(
                zone.Id,
                out ianaId)
            && !string.IsNullOrWhiteSpace(ianaId))
        {
            return ianaId;
        }

        return candidate;
    }

    public static DateTimeOffset GetNextCronOccurrence(
        string cronExpression,
        string timeZoneId,
        DateTimeOffset afterUtc)
    {
        var expression = CronExpression.Parse(
            cronExpression,
            CronFormat.Standard);
        var zone = ResolveTimeZone(timeZoneId);
        return expression
            .GetNextOccurrence(afterUtc.ToUniversalTime(), zone)
            ?.ToUniversalTime()
            ?? throw new InvalidOperationException(
                "The cron expression does not have a reachable next occurrence.");
    }

    public static JobScheduleDueDecision CalculateDue(
        JobScheduleRecord schedule,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var scheduledForUtc = schedule.NextExecutionAtUtc?.ToUniversalTime()
            ?? throw new InvalidOperationException(
                "A due schedule must expose its next execution instant.");
        var observedAtUtc = nowUtc.ToUniversalTime();
        if (scheduledForUtc > observedAtUtc)
        {
            throw new InvalidOperationException(
                "A future schedule cannot be materialized.");
        }

        // EndTime 时间窗口到期：当前时间已超过计划结束时间，直接标记计划完成，不再创建执行。
        // 对应 Admin.NET SysJobTrigger.EndTime，避免在过期窗口外继续触发任务。
        if (schedule.EndTime is { } endTime && observedAtUtc > endTime)
        {
            return new JobScheduleDueDecision(
                false,
                null,
                null,
                endTime.ToUniversalTime());
        }

        if (string.Equals(
                schedule.TriggerKind,
                JobTriggerKinds.OneTime,
                StringComparison.Ordinal))
        {
            return new JobScheduleDueDecision(
                true,
                scheduledForUtc,
                null,
                observedAtUtc);
        }

        if (!string.Equals(
                schedule.TriggerKind,
                JobTriggerKinds.Cron,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(schedule.CronExpression))
        {
            throw new InvalidOperationException(
                "The schedule trigger is not supported.");
        }

        var expression = CronExpression.Parse(
            schedule.CronExpression,
            CronFormat.Standard);
        var zone = ResolveTimeZone(schedule.TimeZoneId);
        var nextAfterScheduled = expression
            .GetNextOccurrence(scheduledForUtc, zone)
            ?.ToUniversalTime()
            ?? throw new InvalidOperationException(
                "The cron expression does not have a reachable next occurrence.");

        // 下一次触发已超出 EndTime 窗口：创建当前到期执行并标记计划完成，不再安排后续触发。
        if (schedule.EndTime is { } cronEndTime && nextAfterScheduled > cronEndTime)
        {
            return new JobScheduleDueDecision(
                true,
                scheduledForUtc,
                null,
                observedAtUtc);
        }

        if (nextAfterScheduled > observedAtUtc)
        {
            return new JobScheduleDueDecision(
                true,
                scheduledForUtc,
                nextAfterScheduled,
                null);
        }

        var nextAfterNow = expression
            .GetNextOccurrence(observedAtUtc, zone)
            ?.ToUniversalTime()
            ?? throw new InvalidOperationException(
                "The cron expression does not have a reachable next occurrence.");

        // Misfire 跳过策略下，如果补偿后的下次触发也超出 EndTime，则标记计划完成。
        if (schedule.EndTime is { } skipEndTime && nextAfterNow > skipEndTime)
        {
            return new JobScheduleDueDecision(
                false,
                null,
                null,
                observedAtUtc);
        }

        if (string.Equals(
                schedule.MisfirePolicy,
                JobMisfirePolicies.Skip,
                StringComparison.Ordinal))
        {
            return new JobScheduleDueDecision(
                false,
                null,
                nextAfterNow,
                null);
        }

        if (!string.Equals(
                schedule.MisfirePolicy,
                JobMisfirePolicies.FireOnce,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The schedule misfire policy is not supported.");
        }

        var latestMissed = expression
            .GetPreviousOccurrence(
                observedAtUtc,
                zone,
                inclusive: true)
            ?.ToUniversalTime()
            ?? scheduledForUtc;
        return new JobScheduleDueDecision(
            true,
            latestMissed,
            nextAfterNow,
            null);
    }

    public static IReadOnlyList<DateTimeOffset> GetNextCronOccurrences(
        string cronExpression,
        string timeZoneId,
        DateTimeOffset afterUtc,
        int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        count = Math.Min(count, 10);
        var expanded = ExpandCronMacro(cronExpression);
        var expression = CronExpression.Parse(expanded, CronFormat.Standard);
        var zone = ResolveTimeZone(timeZoneId);
        var cursor = afterUtc.ToUniversalTime();
        var occurrences = new List<DateTimeOffset>(count);
        for (var index = 0; index < count; index++)
        {
            var next = expression.GetNextOccurrence(cursor, zone)?.ToUniversalTime()
                ?? throw new InvalidOperationException(
                    "The cron expression does not have a reachable next occurrence.");
            occurrences.Add(next);
            cursor = next;
        }

        return occurrences;
    }

    public static string ExpandCronMacro(string cronExpression)
    {
        var candidate = cronExpression?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            throw new InvalidOperationException("The cron expression is required.");
        }

        return candidate.ToLowerInvariant() switch
        {
            "@yearly" or "@annually" => "0 0 1 1 *",
            "@monthly" => "0 0 1 * *",
            "@weekly" => "0 0 * * 0",
            "@daily" => "0 0 * * *",
            "@hourly" => "0 * * * *",
            _ => candidate,
        };
    }

    public static string DescribeCron(string cronExpression)
    {
        var candidate = cronExpression?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            return "jobs.cron.invalid";
        }

        return candidate.ToLowerInvariant() switch
        {
            "@yearly" or "@annually" => "jobs.cron.macro.yearly",
            "@monthly" => "jobs.cron.macro.monthly",
            "@weekly" => "jobs.cron.macro.weekly",
            "@daily" => "jobs.cron.macro.daily",
            "@hourly" => "jobs.cron.macro.hourly",
            _ => "jobs.cron.custom",
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        var normalized = NormalizeTimeZoneId(timeZoneId);
        return string.Equals(normalized, "UTC", StringComparison.Ordinal)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(normalized);
    }
}
