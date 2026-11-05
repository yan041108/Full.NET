using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>来源寿命与任务结果分离，执行保护不能复活已经进入释放阶段的来源。</summary>
[TestClass]
public sealed class ImportSourceRetentionRulesTests
{
    [TestMethod]
    [DataRow("preview_succeeded", 1, false, false, "available", true)]
    [DataRow("preview_succeeded", 0, false, false, "expired", false)]
    [DataRow("preview_failed", -1, false, false, "expired", false)]
    [DataRow("execution_partial", -1, false, false, "expired", false)]
    [DataRow("canceled", -1, false, false, "expired", false)]
    [DataRow("queued", -1, false, false, "retained_for_execution", true)]
    [DataRow("executing", -1, false, false, "retained_for_execution", true)]
    [DataRow("queued", -1, true, false, "releasing", false)]
    [DataRow("preview_succeeded", 1, true, true, "released", false)]
    public void Current_source_state_preserves_in_flight_work_and_honors_durable_release(string status, int offset,
        bool started, bool released, string expected, bool available)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new ImportExportTaskRecord { StatusKey = status, SourceExpiresAtUtc = now.AddMinutes(offset),
            SourceReleaseStartedAtUtc = started ? now.AddMinutes(-1) : null, SourceReleasedAtUtc = released ? now : null };
        var response = ImportSourceRetentionRules.Describe(task, now);
        Assert.AreEqual(expected, response.StateKey); Assert.AreEqual(available, ImportSourceRetentionRules.CanUse(task, now));
        Assert.AreEqual(task.SourceExpiresAtUtc, response.ExpiresAtUtc); Assert.AreEqual(task.SourceReleasedAtUtc, response.ReleasedAtUtc);
    }

    [TestMethod]
    public async Task Expired_source_owner_keeps_error_receipt_reference_and_original_history()
    {
        var now = DateTimeOffset.UtcNow; var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var task = new ImportExportTaskRecord { Id = Guid.CreateVersion7(), SourceFileId = Guid.CreateVersion7(),
            SourceExpiresAtUtc = now.AddDays(-1), ErrorReceiptFileId = Guid.CreateVersion7(), StatusKey = ImportExportTaskStatusKeys.ExecutionPartial,
            SucceededRowCount = 3, NextLineNumber = 5 };
        var queries = Substitute.For<IQueryExecutor>(); queries.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(task);
        var owner = new ImportExportResourceFileOwner(queries, clock);
        Assert.IsFalse(await owner.IsReferencedAsync(task.Id, task.SourceFileId));
        Assert.IsTrue(await owner.IsReferencedAsync(task.Id, task.ErrorReceiptFileId!.Value));
        Assert.IsFalse(await owner.IsReferencedAsync(task.Id, Guid.CreateVersion7()));
        Assert.AreEqual(3, task.SucceededRowCount); Assert.AreEqual(5, task.NextLineNumber);
    }

    [TestMethod]
    [DataRow("days", 0)][DataRow("days", 366)][DataRow("batch", 0)][DataRow("batch", 101)][DataRow("poll", 4)][DataRow("poll", 3601)]
    public void Cleanup_settings_reject_unbounded_or_invalid_values(string setting, int value)
    {
        var options = new ImportExportOptions();
        if (setting == "days") options.SourceRetentionDays = value;
        else if (setting == "batch") options.SourceCleanupBatchSize = value;
        else options.SourceCleanupPollSeconds = value;
        Assert.IsTrue(new ImportExportOptionsValidator().Validate(null, options).Failed);
    }
}
