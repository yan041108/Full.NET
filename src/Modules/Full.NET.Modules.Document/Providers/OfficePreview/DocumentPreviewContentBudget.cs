using Full.NET.Modules.Document.Contracts;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>转换输入与输出按实际读取字节执行预算；输入流始终由调用方持有。</summary>
internal static class DocumentPreviewContentBudget
{
    internal static void CheckDeclaredLength(long length, long maximum, string code)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (length < 0) throw new InvalidDataException("Preview content length is invalid.");
        if (length > maximum) throw new DocumentPreviewContentLimitException(code);
    }

    internal static async Task<MemoryStream> ReadOutputAsync(Stream source, long? declaredLength, long maximum, CancellationToken token)
    {
        if (maximum > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximum));
        CheckDeclaredLength(declaredLength ?? 0, maximum, DocumentErrorCodes.OfficePreviewOutputTooLarge);
        var memory = new MemoryStream();
        try
        {
            using var limited = new DocumentPreviewBudgetStream(source, maximum, DocumentErrorCodes.OfficePreviewOutputTooLarge);
            await limited.CopyToAsync(memory, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (memory.Length == 0) throw new InvalidDataException("Preview output is empty.");
            memory.Position = 0;
            return memory;
        }
        catch { await memory.DisposeAsync().ConfigureAwait(false); throw; }
    }
}

/// <summary>稳定预算失败，不携带内部路径、供应商响应或凭据。</summary>
internal sealed class DocumentPreviewContentLimitException(string code) : IOException("Preview content exceeds the configured size limit.")
{
    internal string Code { get; } = code;
}

/// <summary>只读借用流；读取预算包含不可定位流，释放包装器不会释放调用方的原流。</summary>
internal sealed class DocumentPreviewBudgetStream(Stream source, long maximum, string code) : Stream
{
    private long consumed;
    private bool disposed;
    public override bool CanRead => !disposed && source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var read = source.Read(buffer[..Allowed(buffer.Length)]);
        Record(read); return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); token.ThrowIfCancellationRequested();
        var read = await source.ReadAsync(buffer[..Allowed(buffer.Length)], token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        Record(read); return read;
    }
    private int Allowed(int requested)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        var remaining = maximum - consumed;
        // 只多读一个字节区分刚好到边界与实际超限，禁止先读完整大块再拒绝。
        return remaining >= requested ? requested : (int)remaining + 1;
    }
    private void Record(int read)
    {
        if (read > maximum - consumed) throw new DocumentPreviewContentLimitException(code);
        consumed += read;
    }
    protected override void Dispose(bool disposing) { disposed = true; base.Dispose(disposing); }
}
