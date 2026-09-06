using System.Text;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Retention;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingExportTimeRangePolicyTests
{
    [TestMethod]
    public void Plan_rejects_missing_time_range()
    {
        var policy = CreatePolicy();

        var result = policy.Plan(
            AuditLogExportKind.Access,
            null,
            DateTimeOffset.UtcNow);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AuditingErrorCodes.ExportTimeRangeRequired, result.Error!.Code);
    }

    [TestMethod]
    public void Plan_rejects_range_before_retention_boundary()
    {
        var now = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var policy = CreatePolicy(now, accessRetentionDays: 30);

        var result = policy.Plan(
            AuditLogExportKind.Access,
            now.AddDays(-31),
            now.AddDays(-30).AddHours(-1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AuditingErrorCodes.ExportRetentionBoundaryExceeded, result.Error!.Code);
    }

    [TestMethod]
    public void Plan_accepts_range_within_export_and_retention_limits()
    {
        var now = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var policy = CreatePolicy(now, accessRetentionDays: 30, maximumExportRows: 1000);

        var result = policy.Plan(
            AuditLogExportKind.Access,
            now.AddDays(-1),
            now);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1000, result.Value.MaximumRows);
    }

    private static AuditingExportTimeRangePolicy CreatePolicy(
        DateTimeOffset? now = null,
        int accessRetentionDays = 30,
        int maximumExportRows = 5000) =>
        new(
            Options.Create(new AuditingQueryOptions
            {
                MaximumExportWindowDays = 7,
                MaximumExportRows = maximumExportRows,
            }),
            Options.Create(new AuditingRetentionOptions
            {
                AccessRetentionDays = accessRetentionDays,
                OperationRetentionDays = 365,
                ExceptionRetentionDays = 90,
            }),
            new FixedClock(now ?? DateTimeOffset.UtcNow));

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
