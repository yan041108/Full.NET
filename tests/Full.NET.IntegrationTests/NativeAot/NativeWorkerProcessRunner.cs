using System.Diagnostics;
using System.Text;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>运行一次性原生 Worker 命令，捕获退出码与完整日志用于失败诊断。</summary>
internal static class NativeWorkerProcessRunner
{
    /// <summary>
    /// 启动一次性原生 Worker，执行指定消息版本的退休安全扫描并捕获进程证据。
    /// </summary>
    /// <param name="artifact">待验证的原生 Worker 发布产物。</param>
    /// <param name="provider">本次扫描使用的数据库提供程序。</param>
    /// <param name="connectionString">测试数据库连接字符串。</param>
    /// <param name="messageType">待扫描的稳定消息类型。</param>
    /// <param name="schemaVersion">待检查的消息架构版本。</param>
    /// <param name="timeout">允许原生进程完成扫描的最长时间。</param>
    /// <param name="cancellationToken">用于取消本次进程验证的令牌。</param>
    /// <returns>包含退出码、标准输出、标准错误与日志路径的进程结果。</returns>
    public static async Task<NativeWorkerProcessResult> RunVersionRetirementAsync(
        NativeWorkerArtifact artifact,
        DatabaseProvider provider,
        string connectionString,
        string messageType,
        int schemaVersion,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var logDirectory = Path.Combine(
            artifact.RepositoryRoot,
            "artifacts",
            "native-aot",
            "worker",
            "linux-x64",
            "test-logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(
            logDirectory,
            $"fullnet-native-worker-{provider.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.log");

        var startInfo = new ProcessStartInfo
        {
            FileName = artifact.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = artifact.PublishDirectory,
        };
        startInfo.ArgumentList.Add("--outbox-version-retirement-message-type");
        startInfo.ArgumentList.Add(messageType);
        startInfo.ArgumentList.Add("--outbox-version-retirement-schema-version");
        startInfo.ArgumentList.Add(schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        startInfo.Environment[$"{DatabaseOptions.SectionName}__Provider"] = provider.ToString();
        startInfo.Environment[$"{DatabaseOptions.SectionName}__ConnectionString"] = connectionString;
        startInfo.Environment[$"{DatabaseOptions.SectionName}__CommandTimeoutSeconds"] = "30";
        startInfo.Environment[$"{DatabaseOptions.SectionName}__MySqlGuidStorageMode"] = "Binary16";
        startInfo.Environment["Realtime__Enabled"] = "false";
        // 一次性命令仍会执行宿主启动校验，因此必须提供与常驻 Worker 相同的本地文件和密钥目录边界。
        startInfo.Environment["Files__Local__RootPath"] = Path.Combine(
            Path.GetTempPath(),
            "fullnet-worker-native-aot",
            Guid.NewGuid().ToString("N"));
        startInfo.Environment["DataProtection__KeyRingPath"] = Path.Combine(
            Path.GetTempPath(),
            "fullnet-worker-native-aot-keys",
            Guid.NewGuid().ToString("N"));

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Native Worker 进程。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var interrupted = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await File.WriteAllTextAsync(
                logPath,
                $"STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}",
                new UTF8Encoding(false),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (interrupted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Native Worker 未在 {timeout} 内退出。日志：{logPath}");
        }

        return new NativeWorkerProcessResult(process.ExitCode, stdout, stderr, logPath);
    }
}

internal sealed record NativeWorkerProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string LogPath);
