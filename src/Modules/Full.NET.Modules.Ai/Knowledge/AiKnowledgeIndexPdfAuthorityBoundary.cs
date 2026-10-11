namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>托管事务外 IO 及原会话续租，禁止用于放弃激活写事务；收费消费者须独立冻结回执并结算。</summary>
internal static class AiKnowledgeIndexPdfAuthorityBoundary
{
    // 嵌套 IO 共享同一操作寿命；可放弃外部响应，但不能放弃已经开始的本地证明事务。
    private static readonly AsyncLocal<ProofLifetime?> Current = new();

    internal static Full.NET.Abstractions.Messaging.ICommandTransaction TrackTransactions(Full.NET.Abstractions.Messaging.ICommandTransaction transaction)
        => new TrackedTransaction(transaction);

    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var previous = Current.Value;
        var lifetime = previous ?? new ProofLifetime(); Current.Value = lifetime;
        var io = new CancellationTokenSource(); var delivered = false;
        // 外部 Port 的同步阻塞与取消回调都不能占住工作调用者。
        var pending = AiKnowledgePdfIoBoundary.RunAsync(() => { io.Token.ThrowIfCancellationRequested(); return action(io.Token); });
        try
        {
            var result = await pending.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); delivered = true; return result;
        }
        finally
        {
            try
            {
            if (delivered) io.Dispose();
            else
            {
                // 先关闭事务入口，防止取消清理与一个刚准备开始的事务竞争。
                var proofs = lifetime.Close();
                var cleanup = Task.WhenAll(ObserveAsync(io.CancelAsync()), ObserveAsync(pending));
                try { await cleanup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    var waitingForProof = proofs.Any(proof => !proof.IsCompleted);
                    await Task.WhenAll(proofs.Select(ObserveAsync)).ConfigureAwait(false);
                    if (waitingForProof) token.ThrowIfCancellationRequested();
                    throw new IOException("PDF authority cleanup was not confirmed.");
                }
                finally
                {
                    if (cleanup.IsCompleted) io.Dispose();
                    else _ = DisposeAfterAsync(cleanup, io);
                }
            }
            }
            finally { Current.Value = previous; }
        }
    }
    private sealed class ProofLifetime
    {
        private readonly object gate = new();
        private readonly List<Task> proofs = [];
        private bool closed;
        internal Task<T> StartAsync<T>(Func<Task<T>> action, CancellationToken token)
        {
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (closed) throw new OperationCanceledException("来源证明的操作寿命已关闭。", token);
                var pending = action(); proofs.Add(pending); return pending;
            }
        }
        internal Task[] Close() { lock (gate) { closed = true; return proofs.ToArray(); } }
    }
    private sealed class TrackedTransaction(Full.NET.Abstractions.Messaging.ICommandTransaction inner) : Full.NET.Abstractions.Messaging.ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
            => Current.Value is { } lifetime ? lifetime.StartAsync(() => inner.ExecuteAsync(action, token), token) : inner.ExecuteAsync(action, token);
    }
    private static async Task ObserveAsync(Task pending)
    { try { await pending.ConfigureAwait(false); } catch { } }
    private static async Task DisposeAfterAsync(Task cleanup, CancellationTokenSource io)
    { await ObserveAsync(cleanup).ConfigureAwait(false); io.Dispose(); }
}
