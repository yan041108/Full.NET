using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>处理器结果须证明汇总与原始行身份一致，再进入持久化检查点。</summary>
[TestClass]
public sealed class ImportExportResultBoundaryTests
{
    [TestMethod]
    [DataRow("null")]
    [DataRow("rows-null")]
    [DataRow("row-null")]
    [DataRow("negative-total")]
    [DataRow("negative-valid")]
    [DataRow("negative-invalid")]
    [DataRow("sum")]
    [DataRow("count")]
    [DataRow("valid-count")]
    [DataRow("duplicate")]
    [DataRow("reverse")]
    [DataRow("negative-line")]
    [DataRow("zero-line")]
    [DataRow("limit")]
    public void Invalid_preview_cannot_be_persisted_as_success(string kind)
    {
        StaticImportPreviewResult? result = kind switch
        {
            "null" => null,
            "rows-null" => new(1, 1, 0, null!),
            "row-null" => new(1, 1, 0, [null!]),
            "negative-total" => new(-1, 0, 0, []),
            "negative-valid" => new(1, -1, 2, [new(1, false, "error", null)]),
            "negative-invalid" => new(1, 2, -1, [new(1, true, null, null)]),
            "sum" => new(1, 0, 0, [new(1, false, "error", null)]),
            "count" => new(2, 2, 0, [new(1, true, null, null)]),
            "valid-count" => new(1, 1, 0, [new(1, false, "error", null)]),
            "duplicate" => new(2, 2, 0, [new(1, true, null, null), new(1, true, null, null)]),
            "reverse" => new(2, 2, 0, [new(2, true, null, null), new(1, true, null, null)]),
            "negative-line" => new(1, 1, 0, [new(-1, true, null, null)]),
            "zero-line" => new(1, 1, 0, [new(0, true, null, null)]),
            _ => new(2, 2, 0, [new(1, true, null, null), new(2, true, null, null)]),
        };
        Assert.IsFalse(ImportExportResultBoundary.IsValidPreview(result, kind == "limit" ? 1 : 1000, default));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Valid_preview_supports_empty_and_mixed_validity(bool empty)
    {
        StaticImportPreviewResult preview = empty ? new(0, 0, 0, []) : new(2, 1, 1,
            [new(1, true, null, null), new(2, false, "validation.failed", "invalid")]);
        Assert.IsTrue(ImportExportResultBoundary.IsValidPreview(preview, 1000, default));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("rows-null")]
    [DataRow("row-null")]
    [DataRow("too-many")]
    [DataRow("remaining")]
    [DataRow("duplicate")]
    [DataRow("reverse")]
    [DataRow("zero-line")]
    [DataRow("negative-line")]
    [DataRow("old-line")]
    [DataRow("empty")]
    public void Invalid_batch_cannot_advance_checkpoint(string kind)
    {
        var task = new ImportExportTaskRecord { TotalRows = 3, ValidRowCount = 3, NextLineNumber = kind == "remaining" ? 3 : 0 };
        IReadOnlyList<StaticImportRowExecutionResult> history = kind == "old-line" ? [new(1, true, null, null, null)] : [];
        StaticImportBatchExecutionResult? batch = kind switch
        {
            "null" => null,
            "rows-null" => new(null!),
            "row-null" => new([null!]),
            "duplicate" => new([new(1, true, null, null, null), new(1, true, null, null, null)]),
            "reverse" => new([new(2, true, null, null, null), new(1, true, null, null, null)]),
            "zero-line" => new([new(0, true, null, null, null)]),
            "negative-line" => new([new(-1, true, null, null, null)]),
            "empty" => new([]),
            "too-many" => new([new(1, true, null, null, null), new(2, true, null, null, null)]),
            _ => new([new(1, true, null, null, null)]),
        };
        Assert.IsFalse(ImportExportResultBoundary.IsValidBatch(task, batch, history, kind == "too-many" ? 1 : 2, default, out _));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Valid_batch_supports_noncontiguous_original_lines_and_finished_empty_batch(bool finished)
    {
        var task = new ImportExportTaskRecord { TotalRows = 3, ValidRowCount = 2, NextLineNumber = finished ? 2 : 1 };
        var batch = new StaticImportBatchExecutionResult(finished ? [] : [new(3, false, null, "error", "failed")]);
        Assert.IsTrue(ImportExportResultBoundary.IsValidBatch(task, batch, [new(1, true, null, null, null)], 2, default, out _));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Result_text_budget_includes_prior_checkpoint_text(bool preview)
    {
        var text = new string('x', 8 * 1024 * 1024);
        if (preview) Assert.IsFalse(ImportExportResultBoundary.IsValidPreview(new(1, 0, 1, [new(1, false, "x", text)]), 1000, default));
        else Assert.IsFalse(ImportExportResultBoundary.IsValidBatch(new() { TotalRows = 2, ValidRowCount = 2, NextLineNumber = 1 },
            new([new(2, false, null, "x", "x")]), [new(1, false, null, null, text)], 2, default, out _));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Cancelled_result_checks_propagate_before_reading_rows(bool preview)
    {
        if (preview) Assert.ThrowsExactly<OperationCanceledException>(() => ImportExportResultBoundary.IsValidPreview(null, 1000, new(true)));
        else Assert.ThrowsExactly<OperationCanceledException>(() => ImportExportResultBoundary.IsValidBatch(new(), null, [], 2, new(true), out _));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(4096)]
    public void Preview_keeps_physical_source_line_identity_beyond_data_row_count(int line)
    {
        Assert.IsTrue(ImportExportResultBoundary.IsValidPreview(new(1, 1, 0, [new(line, true, null, null)]), 1000, default));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(4096)]
    public void Execution_keeps_physical_source_line_identity_beyond_data_row_count(int line)
    {
        Assert.IsTrue(ImportExportResultBoundary.IsValidBatch(new() { TotalRows = 1, ValidRowCount = 1 },
            new([new(line, true, Guid.CreateVersion7(), null, null)]), [], 1, default, out _));
    }
}
