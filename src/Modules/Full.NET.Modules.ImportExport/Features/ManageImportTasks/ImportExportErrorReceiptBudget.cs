using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>回执身份及工作簿生成前检查独立预算，不让压缩率掩盖错误文本内存。</summary>
internal static class ImportExportErrorReceiptBudget
{
    internal const int MaxRows = 1000;
    internal const long MaxTextBytes = 16 * 1024 * 1024;
    internal const long MaxOutputBytes = 10 * 1024 * 1024;

    internal static void Validate(IReadOnlyList<StaticImportRowExecutionResult> rows, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (rows.Count > MaxRows) throw new ImportExportErrorReceiptLimitException();
        long bytes = 0;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            bytes += ((long)(row.ErrorCode?.Length ?? 0) + (row.Message?.Length ?? 0)) * sizeof(char);
            if (bytes > MaxTextBytes) throw new ImportExportErrorReceiptLimitException();
        }
    }
}

/// <summary>确定性回执超限必须结束自动处理，不作为未知基础设施故障无限重试。</summary>
internal sealed class ImportExportErrorReceiptLimitException() : IOException("Import error receipt exceeds its configured budget.");

/// <summary>在 ZIP 写入期间限制实际输出并响应取消；流仅由当前渲染器持有。</summary>
internal sealed class ImportExportErrorReceiptOutputStream(CancellationToken token = default) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) { Check(Position + count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(Position + buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(Position + 1); base.WriteByte(value); }
    public override void SetLength(long value) { Check(value); base.SetLength(value); }
    private void Check(long length)
    {
        token.ThrowIfCancellationRequested();
        if (length > ImportExportErrorReceiptBudget.MaxOutputBytes) throw new ImportExportErrorReceiptLimitException();
    }
}
