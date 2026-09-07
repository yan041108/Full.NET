namespace Full.NET.Modules.Reporting.Domain;

/// <summary>限制 ZIP 在写入期间的长度，防止生成完成后才发现文件超限。</summary>
internal sealed class ReportingExportOutputStream : MemoryStream
{
    /// <summary>写入指定字节片段前检查输出预算。</summary>
    /// <param name="buffer">待写入缓冲区。</param>
    /// <param name="offset">片段起点。</param>
    /// <param name="count">片段长度。</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureLength(Position + count);
        base.Write(buffer, offset, count);
    }

    /// <summary>写入跨度前检查输出预算。</summary>
    /// <param name="buffer">待写入字节。</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureLength(Position + buffer.Length);
        base.Write(buffer);
    }

    /// <summary>写入单字节前检查预算。</summary>
    /// <param name="value">待写入字节。</param>
    public override void WriteByte(byte value)
    {
        EnsureLength(Position + 1);
        base.WriteByte(value);
    }

    /// <summary>限制显式扩容。</summary>
    /// <param name="value">目标长度。</param>
    public override void SetLength(long value)
    {
        EnsureLength(value);
        base.SetLength(value);
    }

    /// <summary>超过文件上限时立即停止写入。</summary>
    /// <param name="length">写入后的目标长度。</param>
    private static void EnsureLength(long length)
    {
        if (length > ReportingExportPolicy.MaxExportBytes)
            throw new InvalidDataException("The export file size limit was exceeded.");
    }
}
