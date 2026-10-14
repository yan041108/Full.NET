using System.Diagnostics;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>只管理本次创建的前台转换进程；持续丢弃输出，取消或失败时回收仍在途的进程树。</summary>
internal static class DocumentPreviewProcessLifetime
{
    internal static async Task<int> RunAsync(ProcessStartInfo startInfo, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError || startInfo.UseShellExecute)
            throw new ArgumentException("Preview conversion requires redirected streams and shell execution disabled.", nameof(startInfo));
        using var drainCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new IOException("Preview conversion process could not start.");
        Task stdout = Task.CompletedTask, stderr = Task.CompletedTask;
        var failed = false;
        try
        {
            stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, drainCancellation.Token);
            stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, drainCancellation.Token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        catch { failed = true; throw; }
        finally
        {
            try { await CleanupAsync().ConfigureAwait(false); }
            catch (Exception exception) when (failed && exception is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException or IOException)
            {
                // 保留原始业务取消或转换故障；管道收尾已由独立 finally 保证执行。
            }
        }

        async Task CleanupAsync()
        {
            try
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    // 收尾使用独立有限等待，已取消的业务令牌不能切断自有进程回收。
                    await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            finally
            {
                try { drainCancellation.Cancel(); }
                finally
                {
                    try { process.StandardOutput.Dispose(); }
                    finally
                    {
                        try { process.StandardError.Dispose(); }
                        finally
                        {
                            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                            catch (OperationCanceledException) { }
                            catch (ObjectDisposedException) { }
                            catch (IOException) { }
                        }
                    }
                }
            }
        }
    }
}
