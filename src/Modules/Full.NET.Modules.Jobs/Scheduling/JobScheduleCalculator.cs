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

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        var normalized = NormalizeTimeZoneId(timeZoneId);
        return string.Equals(normalized, "UTC", StringComparison.Ordinal)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(normalized);
    }
}
