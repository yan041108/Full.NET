namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只保护事务外读取及原会话续租，不用于放弃计费派发或激活写事务。</summary>
internal static class AiKnowledgeIndexPdfAuthorityBoundary
{
    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var io = new CancellationTokenSource(); var delivered = false;
        // 外部 Port 的同步阻塞与取消回调都不能占住工作调用者。
        var pending = Task.Run(() => { io.Token.ThrowIfCancellationRequested(); return action(io.Token); });
        try
        {
            var result = await pending.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); delivered = true; return result;
        }
        finally
        {
            if (delivered) io.Dispose();
            else
            {
                var cleanup = Task.WhenAll(ObserveAsync(io.CancelAsync()), ObserveAsync(pending));
                try { await cleanup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (TimeoutException) { throw new IOException("PDF authority cleanup was not confirmed."); }
                finally
                {
                    if (cleanup.IsCompleted) io.Dispose();
                    else _ = DisposeAfterAsync(cleanup, io);
                }
            }
        }
    }
    private static async Task ObserveAsync(Task pending)
    { try { await pending.ConfigureAwait(false); } catch { } }
    private static async Task DisposeAfterAsync(Task cleanup, CancellationTokenSource io)
    { await ObserveAsync(cleanup).ConfigureAwait(false); io.Dispose(); }
}
