using System.Net;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>立即交付双向 RPC 的 SSE 帧，读取时累计字节并沿用响应总期限；正文拥有取消和释放责任。</summary>
internal sealed class BoundedMcpSseContent : HttpContent
{
    private readonly HttpContent inner;
    private readonly long maximumBytes;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationToken lifetimeToken;
    private readonly object gate = new();
    private Task<Stream>? streamTask;
    private int disposed;

    internal BoundedMcpSseContent(HttpContent inner, long maximumBytes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (inner.Headers.ContentLength > maximumBytes)
            throw new HttpRequestException("MCP response exceeds the configured byte budget.");
        this.inner = inner;
        this.maximumBytes = maximumBytes;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(timeout);
        lifetimeToken = lifetime.Token;
        foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override Task<Stream> CreateContentReadStreamAsync() => GetStreamAsync(default);
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => GetStreamAsync(cancellationToken);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, default);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var source = await GetStreamAsync(cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private Task<Stream> GetStreamAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        lock (gate) return streamTask ??= CreateStreamAsync(cancellationToken);
    }

    private async Task<Stream> CreateStreamAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        var source = await inner.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        return new BudgetStream(this, source);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0)
        {
            try { lifetime.Cancel(); }
            finally
            {
                try { inner.Dispose(); }
                finally { lifetime.Dispose(); base.Dispose(disposing); }
            }
        }
        else base.Dispose(disposing);
    }

    /// <summary>源正文仅由外层 Content 释放；流先释放时也归还该所有权，不重复关闭源。</summary>
    private sealed class BudgetStream(BoundedMcpSseContent owner, Stream source) : Stream
    {
        private long read;
        private int closed;
        public override bool CanRead => Volatile.Read(ref closed) == 0 && Volatile.Read(ref owner.disposed) == 0 && source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) != 0 || Volatile.Read(ref owner.disposed) != 0, this);
            if (read > owner.maximumBytes)
                throw new HttpRequestException("MCP response exceeds the configured byte budget.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.lifetimeToken);
            linked.Token.ThrowIfCancellationRequested();
            if (buffer.IsEmpty) return 0;
            var count = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, owner.maximumBytes - read + 1)], linked.Token)
                .ConfigureAwait(false);
            read = checked(read + count);
            if (read > owner.maximumBytes)
                throw new HttpRequestException("MCP response exceeds the configured byte budget.");
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        // SDK 走异步解析；拒绝无法在阻塞期间落实总截止时间的同步读取。
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("MCP SSE must be read asynchronously.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref closed, 1) == 0) owner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
