using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.Modules.ImportExport.Domain;

/// <summary>静态处理器返回值进入任务 JSON 与检查点前，必须满足行身份、计数和文本预算。</summary>
internal static class ImportExportResultBoundary
{
    private const int MaximumRows = 1000;
    private const long MaximumTextBytes = 16 * 1024 * 1024;

    /// <summary>逐行结果应完整覆盖预览总数，汇总不能将无效行伪装为成功。</summary>
    /// <param name="preview">处理器返回的成功载荷；空载荷同样视为契约失败。</param>
    /// <param name="maximumRows">本次预校验配置允许的最大行数。</param>
    /// <param name="token">当前请求取消令牌。</param>
    internal static bool IsValidPreview(StaticImportPreviewResult? preview, int maximumRows, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (preview?.Rows is null || preview.TotalRows < 0 || preview.TotalRows > Math.Min(maximumRows, MaximumRows)
            || preview.ValidRowCount < 0 || preview.InvalidRowCount < 0
            || (long)preview.ValidRowCount + preview.InvalidRowCount != preview.TotalRows
            || preview.Rows.Count != preview.TotalRows) return false;

        var valid = 0;
        var line = 0;
        long bytes = 0;
        foreach (var row in preview.Rows)
        {
            token.ThrowIfCancellationRequested();
            // 行号是处理器保存的源身份；Excel 表头和空白行会使它大于数据行数，不能重新编号。
            if (row is null || row.LineNumber <= line || !AddText(ref bytes, row.ErrorCode, row.Message)) return false;
            line = row.LineNumber;
            if (row.IsValid) valid++;
        }
        token.ThrowIfCancellationRequested();
        return valid == preview.ValidRowCount;
    }

    /// <summary>批次只能推进剩余有效行，原始行号不得重复或倒退；历史文本也占用本次快照预算。</summary>
    /// <param name="task">已领取的任务快照。</param>
    /// <param name="batch">处理器返回的成功载荷。</param>
    /// <param name="existing">已经持久化的逐行结果，兼容缺少行明细的历史检查点。</param>
    /// <param name="batchSize">实际传给处理器的批次上限，不重新读取热配置。</param>
    /// <param name="token">本次执行租约令牌。</param>
    /// <param name="textLimitExceeded">失败是否来自结果文本超限，用于保持既有回执超限机器码。</param>
    internal static bool IsValidBatch(ImportExportTaskRecord task, StaticImportBatchExecutionResult? batch,
        IReadOnlyList<StaticImportRowExecutionResult> existing, int batchSize, CancellationToken token, out bool textLimitExceeded)
    {
        textLimitExceeded = false;
        token.ThrowIfCancellationRequested();
        if (batch?.Rows is null || task.TotalRows < 0 || task.TotalRows > MaximumRows
            || task.ValidRowCount < 0 || task.ValidRowCount > task.TotalRows
            || task.NextLineNumber < 0 || task.NextLineNumber > task.ValidRowCount
            || batch.Rows.Count > batchSize || batch.Rows.Count > task.ValidRowCount - task.NextLineNumber
            || existing.Count > MaximumRows || existing.Count + batch.Rows.Count > MaximumRows
            || (batch.Rows.Count == 0 && task.NextLineNumber < task.ValidRowCount)) return false;

        var line = 0;
        long bytes = 0;
        foreach (var row in existing.Concat(batch.Rows))
        {
            token.ThrowIfCancellationRequested();
            if (row is null || row.LineNumber <= line) return false;
            if (!AddText(ref bytes, row.ErrorCode, row.Message))
            {
                textLimitExceeded = true;
                return false;
            }
            line = row.LineNumber;
        }
        token.ThrowIfCancellationRequested();
        return true;
    }

    /// <summary>按原始 UTF-16 文本计费，不让 JSON 转义或压缩后的大小掩盖输入占用。</summary>
    /// <param name="bytes">当前已累计的原文本字节。</param>
    /// <param name="code">稳定错误码。</param>
    /// <param name="message">处理器返回的行说明。</param>
    private static bool AddText(ref long bytes, string? code, string? message)
    {
        bytes += ((long)(code?.Length ?? 0) + (message?.Length ?? 0)) * sizeof(char);
        return bytes <= MaximumTextBytes;
    }
}
