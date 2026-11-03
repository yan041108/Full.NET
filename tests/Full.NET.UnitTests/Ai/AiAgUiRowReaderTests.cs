using System.Data;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>同一原生读取源覆盖双库整数/时间表示，以及无账本时不能伪造的null用量。</summary>
[TestClass]
public sealed class AiAgUiRowReaderTests
{
    [TestMethod][DataRow(false, false)][DataRow(false, true)][DataRow(true, false)][DataRow(true, true)]
    public void All_public_stream_rows_preserve_sequence_time_and_unknown_usage(bool mysql, bool unknown)
    {
        var now = new DateTimeOffset(2026, 10, 31, 3, 0, 0, TimeSpan.Zero);
        using var table = new DataTable();
        foreach (var name in new[] { "MaxSequence", "Sequence", "InputTokens", "OutputTokens" }) table.Columns.Add(name, typeof(long));
        foreach (var name in new[] { "PayloadVersion", "Attempt" }) table.Columns.Add(name, mysql ? typeof(long) : typeof(int));
        foreach (var name in new[] { "EventType", "Payload", "StepKey", "StatusKey", "ErrorCode", "UsageStatus", "Outcome" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("CreatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        var row = table.NewRow(); row["MaxSequence"] = 205L; row["Sequence"] = 19L; row["PayloadVersion"] = 1; row["Attempt"] = 2;
        row["EventType"] = "run.node_started"; row["Payload"] = "{\"bounded\":true}"; row["StepKey"] = "complete"; row["StatusKey"] = "running";
        row["InputTokens"] = unknown ? DBNull.Value : 17L; row["OutputTokens"] = unknown ? DBNull.Value : 23L;
        row["ErrorCode"] = unknown ? DBNull.Value : "ai.step.failed"; row["UsageStatus"] = unknown ? "unknown" : "exact";
        row["Outcome"] = unknown ? "unknown" : "completed";
        row["CreatedAtUtc"] = mysql ? (object)DateTime.SpecifyKind(now.UtcDateTime, DateTimeKind.Unspecified) : now; table.Rows.Add(row);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        Assert.AreEqual(205L, AiBudgetRowReaders.ReadAgUiSequenceBounds(reader).MaxSequence);
        var item = AiBudgetRowReaders.ReadAgUiEvent(reader); Assert.AreEqual(19L, item.Sequence); Assert.AreEqual(1, item.PayloadVersion);
        Assert.AreEqual("run.node_started", item.EventType); Assert.AreEqual("{\"bounded\":true}", item.Payload); Assert.AreEqual(now, item.CreatedAtUtc);
        var step = AiBudgetRowReaders.ReadAgUiStep(reader); Assert.AreEqual("complete", step.StepKey); Assert.AreEqual(2, step.Attempt); Assert.AreEqual("running", step.StatusKey);
        Assert.AreEqual(unknown ? null : (long?)17, step.InputTokens); Assert.AreEqual(unknown ? null : (long?)23, step.OutputTokens);
        Assert.AreEqual(unknown ? null : "ai.step.failed", step.ErrorCode);
        var budget = AiBudgetRowReaders.ReadAgUiBudget(reader); Assert.AreEqual(step.InputTokens, budget.InputTokens); Assert.AreEqual(step.OutputTokens, budget.OutputTokens);
        Assert.AreEqual(unknown ? "unknown" : "exact", budget.UsageStatus); Assert.AreEqual(unknown ? "unknown" : "completed", budget.Outcome);
    }
}
