using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Knowledge Pdf Source Reader。</summary>
[TestClass]
public sealed class AiKnowledgePdfSourceReaderTests
{
    private static readonly byte[] Source = Encoding.UTF8.GetBytes("%PDF-1.7\n中文原始字节");
    private static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData(Source));

    [TestMethod]
    public async Task Exact_source_is_frozen_only_after_eof_and_owned_stream_disposal()
    {
        var stream = new OwnedStream(Source);
        using var output = await new AiKnowledgePdfSourceReader(new TransactionState()).ReadAsync(stream, Hash.ToUpperInvariant(), Source.Length, default);
        Assert.IsNotNull(output);
        CollectionAssert.AreEqual(Source, output.Bytes.ToArray());
        Assert.AreEqual(1, stream.DisposeCount);
        Assert.IsTrue(stream.EofRead);
    }

    [TestMethod]
    public async Task Partial_reads_keep_raw_bytes_and_disposing_result_clears_retained_memory()
    {
        var stream = new OwnedStream(Source.ToArray()) { MaximumRead = 3 };
        var output = await Reader().ReadAsync(stream, Hash, Source.Length, default);
        Assert.IsNotNull(output);
        var retained = output.Bytes;
        CollectionAssert.AreEqual(Source, retained.ToArray());
        output.Dispose(); output.Dispose();
        Assert.IsTrue(retained.ToArray().All(value => value == 0));
        Assert.Throws<ObjectDisposedException>(() => _ = output.Bytes);
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Maximum_valid_source_is_read_in_bounded_pieces()
    {
        var bytes = new byte[AiKnowledgePdfOutputReader.MaxSourceBytes]; Array.Fill(bytes, (byte)'A');
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes)); var stream = new OwnedStream(bytes);
        using var output = await Reader().ReadAsync(stream, hash, bytes.Length, default);
        Assert.IsNotNull(output); CollectionAssert.AreEqual(bytes, output.Bytes.ToArray());
        Assert.IsTrue(stream.LargestRead <= 32768); Assert.IsTrue(stream.EofRead); Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Real_file_stream_is_closed_before_buffer_delivery()
    {
        var path = Path.Combine(Path.GetTempPath(), "fullnet-pdf-source-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(path, Source);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.Asynchronous);
            using var output = await Reader().ReadAsync(stream, Hash, Source.Length, default);
            Assert.IsNotNull(output); CollectionAssert.AreEqual(Source, output.Bytes.ToArray()); Assert.IsFalse(stream.CanRead);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("short")]
    [DataRow("long")]
    [DataRow("hash")]
    public async Task Actual_length_and_hash_must_match_trusted_source(string mode)
    {
        var bytes = mode == "short" ? Source[..^1] : mode == "long" ? Source.Concat(new byte[] { 1 }).ToArray() : Source.ToArray();
        var stream = new OwnedStream(bytes);
        Assert.IsNull(await Reader().ReadAsync(stream, mode == "hash" ? new string('a', 64) : Hash, Source.Length, default));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    [DataRow(0L, "valid")]
    [DataRow(-1L, "valid")]
    [DataRow(5242881L, "valid")]
    [DataRow(1L, "short")]
    [DataRow(1L, "nonhex")]
    public async Task Invalid_metadata_closes_stream_without_reading(long size, string hashKind)
    {
        var stream = new OwnedStream(Source);
        var hash = hashKind == "short" ? Hash[..^1] : hashKind == "nonhex" ? new string('g', 64) : Hash;
        Assert.IsNull(await Reader().ReadAsync(stream, hash, size, default));
        Assert.AreEqual(0, stream.ReadCount); Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(25001)]
    public async Task Unbounded_configuration_is_rejected_before_read(int timeout)
    {
        var stream = new OwnedStream(Source);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Reader(timeout: timeout).ReadAsync(stream, Hash, Source.Length, default));
        Assert.AreEqual(0, stream.ReadCount); Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Transaction_or_precancellation_prevents_read_but_releases_owned_stream(bool cancellation)
    {
        var state = new TransactionState { HasTransaction = !cancellation }; var stream = new OwnedStream(Source);
        if (cancellation) await Assert.ThrowsAsync<OperationCanceledException>(() => Reader(state).ReadAsync(stream, Hash, Source.Length, new CancellationToken(true)));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Reader(state).ReadAsync(stream, Hash, Source.Length, default));
        Assert.AreEqual(0, stream.ReadCount); Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Ignored_read_cancellation_is_interrupted_by_owned_stream_disposal(bool deadline)
    {
        var started = Signal(); var released = Signal();
        var stream = new OwnedStream(Source)
        {
            OnRead = async (_, _) => { started.TrySetResult(); await released.Task; return 0; },
            OnDispose = () => { released.TrySetResult(); return ValueTask.CompletedTask; }
        };
        using var cancellation = new CancellationTokenSource();
        var work = Reader(timeout: deadline ? 500 : 25000).ReadAsync(stream, Hash, Source.Length, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!deadline) cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Synchronously_blocking_read_does_not_block_the_callers_cancellation_boundary()
    {
        var started = Signal(); using var released = new ManualResetEventSlim();
        var stream = new OwnedStream(Source)
        {
            OnRead = (_, _) => { started.TrySetResult(); released.Wait(TimeSpan.FromSeconds(10)); return ValueTask.FromResult(0); },
            OnDispose = () => { released.Set(); return ValueTask.CompletedTask; }
        };
        using var cancellation = new CancellationTokenSource();
        var work = Reader().ReadAsync(stream, Hash, Source.Length, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Stream_cancellation_callback_cannot_block_the_callers_cancel_or_read_deadline()
    {
        var started = Signal(); using var released = new ManualResetEventSlim();
        var stream = new OwnedStream(Source)
        {
            OnRead = async (_, readToken) =>
            {
                // 先让外层等待注册取消，再注册不合作的 IO 回调，稳定复现回调逆序执行造成的阻塞。
                await Task.Delay(50);
                using var registration = readToken.Register(() => released.Wait(TimeSpan.FromSeconds(10)));
                started.TrySetResult();
                await Task.Run(() => released.Wait(TimeSpan.FromSeconds(10))); return 0;
            },
            OnDispose = () => { released.Set(); return ValueTask.CompletedTask; }
        };
        using var cancellation = new CancellationTokenSource();
        var work = Reader().ReadAsync(stream, Hash, Source.Length, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelling = Task.Run(() => cancellation.Cancel());
        try { await cancelling.WaitAsync(TimeSpan.FromSeconds(2)); }
        finally
        {
            released.Set(); await cancelling.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(8)));
        }
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Stream_cancellation_callback_cannot_block_automatic_deadline()
    {
        using var released = new ManualResetEventSlim();
        var stream = new OwnedStream(Source)
        {
            OnRead = async (_, readToken) =>
            {
                await Task.Delay(50);
                using var registration = readToken.Register(() => released.Wait(TimeSpan.FromSeconds(10)));
                await Task.Run(() => released.Wait(TimeSpan.FromSeconds(10))); return 0;
            },
            OnDispose = () => { released.Set(); return ValueTask.CompletedTask; }
        };
        var work = Reader(timeout: 500).ReadAsync(stream, Hash, Source.Length, default);
        try { await Assert.ThrowsAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(2))); }
        finally
        {
            released.Set();
            try { await work.WaitAsync(TimeSpan.FromSeconds(8)); } catch (OperationCanceledException) { }
        }
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Throwing_stream_cancellation_callback_cannot_skip_owned_stream_release(bool readFault)
    {
        var stream = new OwnedStream(Source);
        stream.OnRead = (memory, readToken) =>
        {
            if (stream.ReadCount == 1)
            {
                _ = readToken.Register(() => throw new IOException("synthetic cancel callback"));
                if (readFault) throw new IOException("synthetic read fault");
                Source.AsMemory(0, Source.Length - 1).CopyTo(memory); return ValueTask.FromResult(Source.Length - 1);
            }
            return ValueTask.FromResult(0);
        };
        if (readFault) await Assert.ThrowsAsync<IOException>(() => Reader().ReadAsync(stream, Hash, Source.Length, default));
        else Assert.IsNull(await Reader().ReadAsync(stream, Hash, Source.Length, default));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Exact_size_still_requires_a_bounded_eof_probe()
    {
        var probe = Signal(); var released = Signal(); var stream = new OwnedStream(Source);
        stream.OnRead = async (memory, _) =>
        {
            if (stream.ReadCount == 1) { Source.AsMemory().CopyTo(memory); return Source.Length; }
            probe.TrySetResult(); await released.Task; return 0;
        };
        stream.OnDispose = () => { released.TrySetResult(); return ValueTask.CompletedTask; };
        var work = Reader(timeout: 500).ReadAsync(stream, Hash, Source.Length, default);
        await probe.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Transaction_started_during_read_or_disposal_prevents_delivery(bool duringDisposal)
    {
        var state = new TransactionState(); var stream = new OwnedStream(Source);
        if (duringDisposal) stream.OnDispose = () => { state.HasTransaction = true; return ValueTask.CompletedTask; };
        else stream.OnRead = (memory, _) => { Source.AsMemory().CopyTo(memory); state.HasTransaction = true; return ValueTask.FromResult(Source.Length); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reader(state).ReadAsync(stream, Hash, Source.Length, default));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Read_fault_is_propagated_after_stream_release()
    {
        var stream = new OwnedStream(Source) { OnRead = (_, _) => throw new IOException("synthetic read fault") };
        await Assert.ThrowsAsync<IOException>(() => Reader().ReadAsync(stream, Hash, Source.Length, default));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Disposal_fault_prevents_delivery_of_otherwise_valid_bytes()
    {
        var stream = new OwnedStream(Source) { OnDispose = () => throw new IOException("synthetic disposal fault") };
        await Assert.ThrowsAsync<IOException>(() => Reader().ReadAsync(stream, Hash, Source.Length, default));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Cancellation_during_disposal_prevents_delivery()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new OwnedStream(Source) { OnDispose = () => { cancellation.Cancel(); return ValueTask.CompletedTask; } };
        await Assert.ThrowsAsync<OperationCanceledException>(() => Reader().ReadAsync(stream, Hash, Source.Length, cancellation.Token));
        Assert.AreEqual(1, stream.DisposeCount);
    }

    [TestMethod]
    public async Task Unconfirmed_read_cleanup_rejects_and_late_read_only_touches_private_scratch()
    {
        var started = Signal(); var released = Signal(); var lateRead = Signal(); Memory<byte> retained = default;
        var stream = new OwnedStream(Source)
        {
            OnRead = async (memory, _) =>
            {
                retained = memory; started.TrySetResult(); await released.Task;
                Source.AsMemory().CopyTo(memory); lateRead.TrySetResult(); return Source.Length;
            }
        };
        using var cancellation = new CancellationTokenSource();
        var work = Reader().ReadAsync(stream, Hash, Source.Length, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => work.WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.AreEqual(1, stream.DisposeCount);
        }
        finally { released.TrySetResult(); }
        await lateRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(retained.Length <= 32768);
        using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (retained.ToArray().Any(value => value != 0)) await Task.Delay(10, drain.Token);
    }

    [TestMethod]
    public async Task Synchronously_blocking_disposal_has_its_own_cleanup_limit()
    {
        using var released = new ManualResetEventSlim(); var exited = Signal();
        var stream = new OwnedStream(Source)
        {
            OnDispose = () =>
            {
                try { released.Wait(TimeSpan.FromSeconds(10)); return ValueTask.CompletedTask; }
                finally { exited.TrySetResult(); }
            }
        };
        try { await Assert.ThrowsAsync<IOException>(() => Reader().ReadAsync(stream, Hash, Source.Length, default).WaitAsync(TimeSpan.FromSeconds(8))); }
        finally { released.Set(); await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.AreEqual(1, stream.DisposeCount);
    }

    private static AiKnowledgePdfSourceReader Reader(TransactionState? state = null, int timeout = 25000) => new(state ?? new(), timeout);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TransactionState : IDataTransactionState { public bool HasTransaction { get; set; } }

    private sealed class OwnedStream(byte[] source) : MemoryStream(source, false)
    {
        internal int DisposeCount { get; private set; }
        internal bool EofRead { get; private set; }
        internal int ReadCount { get; private set; }
        internal int LargestRead { get; private set; }
        internal int MaximumRead { get; init; } = int.MaxValue;
        internal Func<Memory<byte>, CancellationToken, ValueTask<int>>? OnRead { get; set; }
        internal Func<ValueTask>? OnDispose { get; set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            LargestRead = Math.Max(LargestRead, buffer.Length);
            var count = OnRead is null ? await base.ReadAsync(buffer[..Math.Min(buffer.Length, MaximumRead)], cancellationToken) : await OnRead(buffer, cancellationToken);
            if (count == 0) EofRead = true;
            return count;
        }
        public override async ValueTask DisposeAsync()
        {
            DisposeCount++;
            try { if (OnDispose is not null) await OnDispose(); }
            finally { await base.DisposeAsync(); }
        }
    }
}
