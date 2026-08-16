using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;
using Full.NET.Modules.Jobs.Scheduling;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobScheduleCalculatorTests
{
    [TestMethod]
    public void NormalizeTimeZoneId_ConvertsWindowsIdToCanonicalIanaId()
    {
        var normalized = JobScheduleCalculator.NormalizeTimeZoneId(
            "Eastern Standard Time");

        Assert.AreEqual("America/New_York", normalized);
    }

    [TestMethod]
    public void NormalizeTimeZoneId_RejectsUnknownId()
    {
        Assert.ThrowsExactly<TimeZoneNotFoundException>(() =>
            JobScheduleCalculator.NormalizeTimeZoneId(
                "Full.NET/Unknown-Time-Zone"));
    }

    [TestMethod]
    public void GetNextCronOccurrence_AdjustsSpringDstGapToNextValidInstant()
    {
        var next = JobScheduleCalculator.GetNextCronOccurrence(
            "30 2 * * *",
            "America/New_York",
            Utc(2026, 3, 7, 7, 31));

        Assert.AreEqual(Utc(2026, 3, 8, 7, 0), next);
    }

    [TestMethod]
    public void GetNextCronOccurrence_DoesNotDuplicateFixedTimeDuringAutumnOverlap()
    {
        var first = JobScheduleCalculator.GetNextCronOccurrence(
            "30 1 * * *",
            "America/New_York",
            Utc(2026, 11, 1, 4, 0));
        var second = JobScheduleCalculator.GetNextCronOccurrence(
            "30 1 * * *",
            "America/New_York",
            first);

        Assert.AreEqual(Utc(2026, 11, 1, 5, 30), first);
        Assert.AreEqual(Utc(2026, 11, 2, 6, 30), second);
    }

    [TestMethod]
    public void CalculateDue_CompletesOneTimeScheduleAfterSingleExecution()
    {
        var now = Utc(2026, 7, 31, 0, 10);
        var decision = JobScheduleCalculator.CalculateDue(
            Schedule(
                JobTriggerKinds.OneTime,
                JobMisfirePolicies.FireOnce,
                nextExecutionAtUtc: Utc(2026, 7, 31, 0, 5)),
            now);

        Assert.IsTrue(decision.CreateExecution);
        Assert.AreEqual(Utc(2026, 7, 31, 0, 5), decision.ScheduledForUtc);
        Assert.IsNull(decision.NextExecutionAtUtc);
        Assert.AreEqual(now, decision.CompletedAtUtc);
    }

    [TestMethod]
    public void CalculateDue_ExecutesNormallyLateCronOccurrence()
    {
        var decision = JobScheduleCalculator.CalculateDue(
            Schedule(
                JobTriggerKinds.Cron,
                JobMisfirePolicies.Skip,
                nextExecutionAtUtc: Utc(2026, 7, 31, 0, 5)),
            Utc(2026, 7, 31, 0, 5, 20));

        Assert.IsTrue(decision.CreateExecution);
        Assert.AreEqual(Utc(2026, 7, 31, 0, 5), decision.ScheduledForUtc);
        Assert.AreEqual(
            Utc(2026, 7, 31, 0, 6),
            decision.NextExecutionAtUtc);
        Assert.IsNull(decision.CompletedAtUtc);
    }

    [TestMethod]
    public void CalculateDue_SkipDropsBacklogWhenMultipleCronPeriodsWereMissed()
    {
        var decision = JobScheduleCalculator.CalculateDue(
            Schedule(
                JobTriggerKinds.Cron,
                JobMisfirePolicies.Skip,
                nextExecutionAtUtc: Utc(2026, 7, 31, 0, 1)),
            Utc(2026, 7, 31, 0, 5, 20));

        Assert.IsFalse(decision.CreateExecution);
        Assert.IsNull(decision.ScheduledForUtc);
        Assert.AreEqual(
            Utc(2026, 7, 31, 0, 6),
            decision.NextExecutionAtUtc);
    }

    [TestMethod]
    public void CalculateDue_FireOnceCollapsesBacklogToLatestMissedOccurrence()
    {
        var decision = JobScheduleCalculator.CalculateDue(
            Schedule(
                JobTriggerKinds.Cron,
                JobMisfirePolicies.FireOnce,
                nextExecutionAtUtc: Utc(2026, 7, 31, 0, 1)),
            Utc(2026, 7, 31, 0, 5, 20));

        Assert.IsTrue(decision.CreateExecution);
        Assert.AreEqual(Utc(2026, 7, 31, 0, 5), decision.ScheduledForUtc);
        Assert.AreEqual(
            Utc(2026, 7, 31, 0, 6),
            decision.NextExecutionAtUtc);
    }

    [TestMethod]
    public void ExpandCronMacro_MapsDailyMacroToFivePartExpression()
    {
        Assert.AreEqual("0 0 * * *", JobScheduleCalculator.ExpandCronMacro("@daily"));
    }

    [TestMethod]
    public void DescribeCron_ReturnsStableMachineCodeForMacro()
    {
        Assert.AreEqual("jobs.cron.macro.hourly", JobScheduleCalculator.DescribeCron("@hourly"));
    }

    [TestMethod]
    public void GetNextCronOccurrences_ReturnsRequestedCount()
    {
        var occurrences = JobScheduleCalculator.GetNextCronOccurrences(
            "0 9 * * *",
            "UTC",
            Utc(2026, 8, 17, 0, 0),
            3);

        Assert.HasCount(3, occurrences);
        Assert.IsTrue(occurrences[1] > occurrences[0]);
    }

    private static JobScheduleRecord Schedule(
        string triggerKind,
        string misfirePolicy,
        DateTimeOffset nextExecutionAtUtc) =>
        new()
        {
            TriggerKind = triggerKind,
            CronExpression = triggerKind == JobTriggerKinds.Cron
                ? "* * * * *"
                : null,
            TimeZoneId = "UTC",
            MisfirePolicy = misfirePolicy,
            NextExecutionAtUtc = nextExecutionAtUtc,
        };

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second = 0) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);
}
