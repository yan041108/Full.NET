using Full.NET.Modules.Auditing;
using Full.NET.Modules.Auditing.Contracts;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditLogTrendPlannerTests
{
    [TestMethod]
    public void Plan_auto_selects_bucket_size_within_limit()
    {
        var fromUtc = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        var toUtc = fromUtc.AddHours(24);

        var result = AuditLogTrendPlanner.Plan(fromUtc, toUtc, null, 96);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(15, result.Value.BucketSizeMinutes);
        Assert.AreEqual(96, result.Value.ExpectedBucketCount);
    }

    [TestMethod]
    public void Plan_rejects_bucket_count_above_limit()
    {
        var fromUtc = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        var toUtc = fromUtc.AddDays(7);

        var result = AuditLogTrendPlanner.Plan(fromUtc, toUtc, 1, 96);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AuditingErrorCodes.TrendBucketLimitExceeded, result.Error!.Code);
    }

    [TestMethod]
    public void Plan_rejects_invalid_bucket_size()
    {
        var fromUtc = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        var toUtc = fromUtc.AddHours(1);

        var result = AuditLogTrendPlanner.Plan(fromUtc, toUtc, 0, 96);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AuditingErrorCodes.TrendBucketSizeInvalid, result.Error!.Code);
    }
}
