using System.Security.Cryptography;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>独占的 PDF 原字节；摘要与授权仍须由调用方绑定权威来源。</summary>
internal sealed class AiKnowledgePdfSourceBuffer : IDisposable
{
    private readonly object gate = new();
    private byte[]? bytes;

    internal AiKnowledgePdfSourceBuffer(int size) => bytes = new byte[size];
    internal ReadOnlyMemory<byte> Bytes
    {
        get { lock (gate) return bytes ?? throw new ObjectDisposedException(nameof(AiKnowledgePdfSourceBuffer)); }
    }

    internal void Write(ReadOnlySpan<byte> source, int offset)
    {
        // 取消路径先撤销写入资格，再等待不合作的流；迟到读取不能复活已清空的结果。
        lock (gate)
        {
            if (bytes is null) throw new ObjectDisposedException(nameof(AiKnowledgePdfSourceBuffer));
            source.CopyTo(bytes.AsSpan(offset, source.Length));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (bytes is null) return;
            CryptographicOperations.ZeroMemory(bytes); bytes = null;
        }
    }
}

/// <summary>取得并释放 Files 交付的独占流；不读取存储路径，也不授予解析或外发权限。</summary>
internal sealed class AiKnowledgePdfSourceReader(IDataTransactionState transactions, int timeoutMs = 25000)
{
    private const int ScratchSize = 32768;
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(5);

    /// <summary>接管流的释放责任；精确大小、摘要、EOF 和清理全部通过才交付原字节。</summary>
    /// <remarks>结果由消费者独占并释放；取消不代表流已停止，无法确认清理时传播故障而不交付结果。</remarks>
    internal async Task<AiKnowledgePdfSourceBuffer?> ReadAsync(Stream ownedSource, string trustedHash, long trustedSize, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(ownedSource);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        // 外层期限不交给流注册回调；流的阻塞或抛异常回调只能进入独立清理任务。
        var io = new CancellationTokenSource();
        AiKnowledgePdfSourceBuffer? output = null; Task<bool>? reading = null; var ready = false;
        try
        {
            token.ThrowIfCancellationRequested(); NoTransaction();
            if (timeoutMs is < 1 or > 25000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            deadline.CancelAfter(timeoutMs);
            if (trustedSize is < 1 or > AiKnowledgePdfOutputReader.MaxSourceBytes || trustedHash is not { Length: 64 }
                || !trustedHash.All(char.IsAsciiHexDigit)) return null;
            output = new((int)trustedSize);
            // 隔离同步阻塞的 ReadAsync 实现，外层 WaitAsync 仍能响应取消；不使用共享或池化字节数组。
            reading = Task.Run(() => ReadOwnedAsync(ownedSource, output, (int)trustedSize, deadline.Token, io.Token));
            if (!await reading.WaitAsync(deadline.Token).ConfigureAwait(false)) return null;
            deadline.Token.ThrowIfCancellationRequested(); NoTransaction();
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(output.Bytes.Span)), trustedHash, StringComparison.OrdinalIgnoreCase)) return null;
            deadline.Token.ThrowIfCancellationRequested(); NoTransaction(); ready = true;
        }
        finally
        {
            if (!ready) { output?.Dispose(); deadline.Cancel(); }
            try { await CloseOwnedAsync(ownedSource, reading, io, !ready).ConfigureAwait(false); }
            catch { output?.Dispose(); deadline.Cancel(); throw; }
        }
        try
        {
            // 释放也可能等待、失败、取消或改变事务状态，不能交付释放前的验证结果。
            deadline.Token.ThrowIfCancellationRequested(); NoTransaction(); return output;
        }
        catch { output?.Dispose(); throw; }
    }

    private async Task<bool> ReadOwnedAsync(Stream source, AiKnowledgePdfSourceBuffer output, int size, CancellationToken token, CancellationToken ioToken)
    {
        // 只有该读取任务持有暂存区；即便清理超时，结果数组已撤销，迟到 IO 也不能接触其他请求。
        var scratch = new byte[ScratchSize]; var offset = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested(); NoTransaction();
                var requested = Math.Min(scratch.Length, size - offset + 1);
                var count = await source.ReadAsync(scratch.AsMemory(0, requested), ioToken).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); NoTransaction();
                if (count == 0) return offset == size;
                if (count < 0 || count > requested || count > size - offset) return false;
                output.Write(scratch.AsSpan(0, count), offset); offset += count;
            }
        }
        finally { CryptographicOperations.ZeroMemory(scratch); }
    }

    private static async Task CloseOwnedAsync(Stream source, Task? reading, CancellationTokenSource io, bool cancel)
    {
        // DisposeAsync 的同步部分同样可能阻塞；独立清理预算不依赖已经取消的读取令牌。
        var cancelling = cancel ? io.CancelAsync() : Task.CompletedTask;
        var closing = Task.Run(async () => await source.DisposeAsync().ConfigureAwait(false));
        var cleanup = Task.WhenAll(closing, ObserveAsync(cancelling), reading is null ? Task.CompletedTask : ObserveAsync(reading));
        try { await cleanup.WaitAsync(CleanupLimit).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // 无法强制终止任意 Stream；观察迟到异常，拒绝交付，不宣称资源已释放。
            throw new IOException("PDF source cleanup was not confirmed.");
        }
        catch { throw new IOException("PDF source disposal failed."); }
        finally
        {
            // 超时仍可能有回调或读操作运行；只在它们退出后释放 IO 令牌，不竞态销毁其注册资源。
            if (cleanup.IsCompleted) io.Dispose();
            else _ = DisposeAfterAsync(cleanup, io);
        }
    }

    private static async Task DisposeAfterAsync(Task cleanup, CancellationTokenSource io)
    { await ObserveAsync(cleanup).ConfigureAwait(false); io.Dispose(); }

    private static async Task ObserveAsync(Task work)
    {
        // 原读取失败已由调用边界传播，或已因取消/清理超时拒绝；此处只回收迟到任务的异常。
        try { await work.ConfigureAwait(false); } catch { }
    }

    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("PDF source reading cannot hold a database transaction."); }
}
