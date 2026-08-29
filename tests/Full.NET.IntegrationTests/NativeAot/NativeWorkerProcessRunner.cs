using System.Diagnostics;
using System.Text;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>运行一次性原生 Worker 命令，捕获退出码与完整日志用于失败诊断。</summary>
internal static class NativeWorkerProcessRunner
{
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
