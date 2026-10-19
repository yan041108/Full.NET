using Full.NET.Benchmarks.Logging;

namespace Full.NET.UnitTests.Logging;

/// <summary>单元测试（日志）：Logging Request Evidence。</summary>
[TestClass]
public sealed class LoggingRequestEvidenceTests
{
    [TestMethod]
    public async Task WarmupBarrierWaitsForConfirmationAndHonoursCancellation()
    {
        var directory = Directory.CreateTempSubdirectory("fullnet-warmup-boundary-").FullName;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var wait = LoggingRequestWarmupBarrier.WaitAsync(directory, deadline.Token);
            Assert.IsFalse(wait.IsCompleted);
            await LoggingRequestWarmupBarrier.ConfirmAsync(directory, deadline.Token);
            await wait;
            File.Delete(Path.Combine(directory, LoggingRequestWarmupBarrier.FileName));
            using var cancel = new CancellationTokenSource();
            var cancelled = LoggingRequestWarmupBarrier.WaitAsync(directory, cancel.Token);
            await cancel.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
            Assert.IsFalse(File.Exists(Path.Combine(directory, LoggingRequestWarmupBarrier.FileName)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void CapacityProfilesKeepRatesCountsAndDeadlinesBounded()
    {
        var comparison = LoggingRequestLoadProfile.Comparison;
        Assert.AreEqual(5000, comparison.Requests);
        Assert.AreEqual(500, comparison.RequestsPerSecond);
        Assert.AreEqual(30000, comparison.SendBudgetMilliseconds);
        foreach (var rate in new[] { 500, 1000 })
        {
            var load = LoggingRequestLoadProfile.Capacity(rate);
            Assert.AreEqual(rate * 30, load.Requests);
            Assert.AreEqual(rate, load.RequestsPerSecond);
            Assert.AreEqual(50000, load.SendBudgetMilliseconds);
            Assert.AreEqual(200, load.WarmupRequests);
            Assert.IsTrue(load.MaximumBrokerRecords > load.Requests + load.WarmupRequests
                && load.MaximumBrokerRecords < 60000);
        }
        foreach (var rate in new[] { -1, 0, 499, 501, 1001, int.MaxValue })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LoggingRequestLoadProfile.Capacity(rate));
    }


    [TestMethod]
    public void ThirtySecondCapacitySamplesRetainFullPopulation()
    {
        var samples = Enumerable.Repeat(1d, 30000).ToArray();
        var statistics = LoggingRequestEvidence.Calculate(samples, 0, 30000, 30000, 0, 0, 0);
        Assert.AreEqual(30000, statistics.SampleCount);
        Assert.AreEqual(1d, statistics.P99Milliseconds);
        LoggingRequestEvidence.ValidateSustainedWindow(30000, 1000, 30010, 50000);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LoggingRequestEvidence.ValidateSustainedWindow(30000, 1000, 30010, double.NaN));
        LoggingRequestEvidence.ValidateSustainedWindow(30000, 1000, 29999);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LoggingRequestEvidence.ValidateSustainedWindow(60001, 1000, 60000));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LoggingRequestEvidence.Calculate(new double[60001], 0, 0, 0, 0, 0, 0));
    }
    [TestMethod]
    public void PacedScheduleHasFixedSpanAndRejectsInvalidInputs()
    {
        Assert.AreEqual(0d, LoggingRequestEvidence.ScheduledMilliseconds(0, 500));
        Assert.AreEqual(9998d, LoggingRequestEvidence.ScheduledMilliseconds(4999, 500));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LoggingRequestEvidence.ScheduledMilliseconds(-1, 500));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LoggingRequestEvidence.ScheduledMilliseconds(1, 0));
    }

    [TestMethod]
    public void SustainedEvidenceRejectsShortOrUnboundedWindows()
    {
        LoggingRequestEvidence.ValidateSustainedWindow(5000, 500, 10010);
        foreach (var invalid in new[] { 100d, 9997d, 30001d, double.NaN, double.PositiveInfinity })
            Assert.ThrowsExactly<InvalidOperationException>(() => LoggingRequestEvidence.ValidateSustainedWindow(5000, 500, invalid));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LoggingRequestEvidence.ValidateSustainedWindow(0, 500, 10000));
    }

    [TestMethod]
    public void CompleteEvidenceProducesNearestRankPercentiles()
    {
        var result = LoggingRequestEvidence.Calculate([4, 1, 3, 2], 0, 4, 4, 0, 4, 4);
        Assert.AreEqual(2d, result.P50Milliseconds);
        Assert.AreEqual(4d, result.P99Milliseconds);
    }

    [TestMethod]
    public void MissingLogsProjectionsUnexpectedResponsesAndDropsCannotPass()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => LoggingRequestEvidence.Calculate([1], 1, 1, 1, 0, 0, 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => LoggingRequestEvidence.Calculate([1], 0, 1, 0, 0, 0, 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => LoggingRequestEvidence.Calculate([1], 0, 1, 1, 1, 0, 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => LoggingRequestEvidence.Calculate([1], 0, 1, 1, 0, 1, 0));
    }

    [TestMethod]
    public void NonFiniteOrNegativeLatencyCannotPass()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
            Assert.ThrowsExactly<ArgumentException>(() => LoggingRequestEvidence.Calculate([invalid], 0, 0, 0, 0, 0, 0));
    }
}
