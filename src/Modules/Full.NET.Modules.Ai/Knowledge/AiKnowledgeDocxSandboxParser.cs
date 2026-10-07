using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>仅由宿主可信配置提供的执行路径和不可变镜像；不接受业务请求中的路径。</summary>
internal sealed record AiKnowledgeDocxSandboxOptions(string NodeExecutablePath, string CliScriptPath, string ImageId, int TimeoutMs = 25000);

/// <summary>私有隔离解析桥接；调用者负责有界取得来源、授权及原会话复核。</summary>
internal sealed class AiKnowledgeDocxSandboxParser(IDataTransactionState transactions, AiKnowledgeDocxSandboxOptions options)
{
    /// <summary>首次等待前冻结实际原字节，绑定摘要并等待完整退出；不缓存授权。</summary>
    internal async Task<AiKnowledgeParsedDocx?> ParseAsync(ReadOnlyMemory<byte> authorizedSource, string trustedHash, int trustedSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); NoTransaction();
        ValidateOptions();
        if (trustedSize is < 1 or > AiKnowledgeDocxOutputReader.MaxSourceBytes || trustedHash is not { Length: 64 }
            || !trustedHash.All(char.IsAsciiHexDigit) || authorizedSource.Length != trustedSize) return null;
        var hash = trustedHash.ToLowerInvariant(); var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(options.TimeoutMs);
        // 不等待任意 Stream：其实现可能忽略取消；调用方的来源读取必须在自己的可中止边界完成。
        var bytes = authorizedSource.ToArray();
        try
        {
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash) return null;
            deadline.Token.ThrowIfCancellationRequested(); NoTransaction();
            var remaining = options.TimeoutMs - (int)clock.ElapsedMilliseconds;
            if (remaining < 1) throw new OperationCanceledException(deadline.Token);
            var document = await ExecuteAsync(bytes, hash, remaining, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested(); NoTransaction(); return document;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private async Task<AiKnowledgeParsedDocx?> ExecuteAsync(ReadOnlyMemory<byte> bytes, string hash, int remainingMs, CancellationToken token)
    {
        using var process = new Process { StartInfo = StartInfo(hash, bytes.Length, remainingMs) };
        using var io = CancellationTokenSource.CreateLinkedTokenSource(token);
        var tasks = new List<Task>(); Task<byte[]>? output = null, errors = null; var started = false;
        try
        {
            token.ThrowIfCancellationRequested(); NoTransaction();
            try { started = process.Start(); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            { throw new IOException("fn_ai_docx_sandbox_start_failed"); }
            if (!started) throw new IOException("fn_ai_docx_sandbox_start_failed");
            output = ReadBoundedAsync(process.StandardOutput.BaseStream, AiKnowledgeDocxOutputReader.MaxWireBytes, io.Token);
            errors = ReadBoundedAsync(process.StandardError.BaseStream, 4096, io.Token);
            tasks.AddRange([output, errors, process.WaitForExitAsync(io.Token), WriteAsync(process.StandardInput.BaseStream, bytes, io.Token)]);
            // 任一流越界立即进入取消清理，不能等待一个仍在无限写入或不退出的子进程。
            var pending = new List<Task>(tasks);
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                await completed.ConfigureAwait(false); pending.Remove(completed);
                token.ThrowIfCancellationRequested(); NoTransaction();
            }
            if (process.ExitCode != 0 || (await errors.ConfigureAwait(false)).Length != 0 || (await output.ConfigureAwait(false)).Length == 0) return null;
            using var wire = new MemoryStream(await output.ConfigureAwait(false), false);
            return (await AiKnowledgeDocxOutputReader.ReadAsync(wire, hash, bytes.Length, token).ConfigureAwait(false)).Document;
        }
        catch (InvalidDataException) { return null; }
        catch (IOException) when (started) { token.ThrowIfCancellationRequested(); return null; }
        finally
        {
            if (started)
            {
                // EOF 是跨平台的取消协议；给控制器独立清理预算，不用 Windows kill 代替容器归属核对。
                io.Cancel();
                // 先关闭已拒绝的输出管道以解除写入背压，再通知 EOF；Windows 同步写入不能只靠事件回调取消。
                process.StandardOutput.Dispose(); process.StandardError.Dispose();
                try { await StopAsync(process).ConfigureAwait(false); }
                finally
                {
                    process.StandardInput.Dispose(); process.StandardOutput.Dispose(); process.StandardError.Dispose();
                    try
                    {
                        if (tasks.Count > 0)
                        {
                            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                            catch (TimeoutException) { throw new IOException("fn_ai_docx_sandbox_io_unconfirmed"); }
                            catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException or ObjectDisposedException) { }
                        }
                    }
                    finally
                    {
                        if (output?.IsCompletedSuccessfully == true) CryptographicOperations.ZeroMemory(await output.ConfigureAwait(false));
                        if (errors?.IsCompletedSuccessfully == true) CryptographicOperations.ZeroMemory(await errors.ConfigureAwait(false));
                    }
                }
            }
        }
    }

    private ProcessStartInfo StartInfo(string hash, int size, int timeout)
    {
        var start = new ProcessStartInfo(options.NodeExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { options.CliScriptPath, "--image-id", options.ImageId, "--content-hash", hash,
            "--size-bytes", size.ToString(CultureInfo.InvariantCulture), "--timeout-ms", timeout.ToString(CultureInfo.InvariantCulture),
            "--run-id", Guid.NewGuid().ToString("D"), "--input-mode", "framed-v1" }) start.ArgumentList.Add(argument);
        // 控制器只继承可信 OS/Docker 控制配置，禁止模型、数据库凭据及 NODE_OPTIONS 等代码注入环境。
        start.Environment.Clear();
        foreach (var key in new[] { "PATH", "SYSTEMROOT", "COMSPEC", "USERPROFILE", "HOME",
            "DOCKER_HOST", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "DOCKER_CONFIG" })
            if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        return start;
    }

    private static async Task WriteAsync(Stream input, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        await input.WriteAsync(bytes, token).ConfigureAwait(false);
        await input.FlushAsync(token).ConfigureAwait(false);
        // 权威长度界定 DOCX 帧；成功退出前保留 stdin，关闭仅用于取消和失败清理。
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var memory = new MemoryStream(); var buffer = new byte[32768];
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)memory.Length)), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); if (count == 0) return memory.ToArray();
                if (count > limit - memory.Length) throw new InvalidDataException("fn_ai_docx_sandbox_output_limit");
                memory.Write(buffer, 0, count);
            }
        }
        finally { CryptographicOperations.ZeroMemory(buffer); CryptographicOperations.ZeroMemory(memory.GetBuffer()); }
    }

    private static async Task StopAsync(Process process)
    {
        try { process.StandardInput.Close(); }
        catch (IOException) { }
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // 控制器异常失联时只终止本次父进程；容器内硬时限仍有效，但未确认清理绝不能交付逻辑文档。
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            using var terminal = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(terminal.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw new IOException("fn_ai_docx_sandbox_shutdown_unconfirmed"); }
            throw new IOException("fn_ai_docx_sandbox_cleanup_unconfirmed");
        }
    }

    private void ValidateOptions()
    {
        if (!Path.IsPathFullyQualified(options.NodeExecutablePath) || !File.Exists(options.NodeExecutablePath)
            || !Path.IsPathFullyQualified(options.CliScriptPath) || !File.Exists(options.CliScriptPath)
            || options.ImageId is not { Length: 71 } || !options.ImageId.StartsWith("sha256:", StringComparison.Ordinal)
            || !options.ImageId.Skip(7).All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f')
            || options.TimeoutMs is < 1 or > 30000)
            throw new ArgumentException("fn_ai_docx_sandbox_invalid_configuration", nameof(options));
    }

    private void NoTransaction()
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("fn_ai_docx_sandbox_transaction_forbidden");
    }
}
