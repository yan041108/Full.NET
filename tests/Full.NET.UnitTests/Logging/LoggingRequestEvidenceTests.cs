using Full.NET.Benchmarks.Logging;

namespace Full.NET.UnitTests.Logging;

[TestClass]
public sealed class LoggingRequestEvidenceTests
{
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
