using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>启动原生 Worker 常驻进程，并保留启动、后台循环与停止阶段的完整日志。</summary>
internal sealed class NativeWorkerProcessHost : IAsyncDisposable
{
    private static readonly string[] FatalLogMarkers =
    [
        "MissingMethodException",
        "TypeInitializationException",
        "JsonSerializerIsReflectionDisabled",
        "IL3050",
        "IL2026",
        "Unhandled exception",
        "Outbox polling iteration failed",
        "Failed Outbox message",
        "Outbox backlog sampling failed; message processing will continue",
        "Outbox retention iteration failed",
        "Database capacity unavailable",
        "Job execution polling iteration failed",
        "Job execution backlog sampling failed; execution polling will continue",
        "Files cleanup iteration failed",
        "Files upload reconciliation iteration failed",
        "Files reference claim reconciliation iteration failed",
        "Workflow todo timeout worker iteration failed.",
    ];

    private readonly Process _process;
    private readonly StreamWriter _logWriter;
    private readonly SemaphoreSlim _logWriteGate;
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private bool _disposed;

    private NativeWorkerProcessHost(
        Process process,
        string logFilePath,
        StreamWriter logWriter,
        SemaphoreSlim logWriteGate,
        Task stdoutPump,
        Task stderrPump,
        Uri baseAddress)
    {
        _process = process;
        _logWriter = logWriter;
        _logWriteGate = logWriteGate;
        _stdoutPump = stdoutPump;
        _stderrPump = stderrPump;
        LogFilePath = logFilePath;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public string LogFilePath { get; }

    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    public static async Task<NativeWorkerProcessHost> StartAsync(
        NativeWorkerArtifact artifact,
        DatabaseProvider provider,
        string connectionString,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken = default,
        string? filesRootPath = null,
        bool enableFilesUploadReconciliation = false,
        bool enableFilesCleanup = false,
        bool enableFilesReferenceClaimReconciliation = false,
        IReadOnlyDictionary<string, string?>? additionalSettings = null)
    {
        var port = GetFreeTcpPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/");
        var logDirectory = Path.Combine(
            artifact.RepositoryRoot,
            "artifacts",
            "native-aot",
            "worker",
            "linux-x64",
            "test-logs");
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(
            logDirectory,
            $"fullnet-native-worker-runtime-{provider.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.log");

        var startInfo = new ProcessStartInfo
        {
            FileName = artifact.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = artifact.PublishDirectory,
        };
        foreach (var pair in BuildEnvironment(
            artifact.RepositoryRoot,
            provider,
            connectionString,
            baseAddress,
            filesRootPath,
            enableFilesUploadReconciliation,
            enableFilesCleanup,
            enableFilesReferenceClaimReconciliation,
            additionalSettings))
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var logWriter = new StreamWriter(
            new FileStream(
                logFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read),
            new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        var logWriteGate = new SemaphoreSlim(1, 1);
        Process process;
        try
        {
            // 文件创建失败时不启动进程；启动失败则释放已经取得的日志资源。
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 Native Worker 常驻进程。");
        }
        catch
        {
            await logWriter.DisposeAsync().ConfigureAwait(false);
            logWriteGate.Dispose();
            throw;
        }

        var stdoutPump = PumpStreamAsync(
            "STDOUT",
            process.StandardOutput,
            logWriter,
            logWriteGate);
        var stderrPump = PumpStreamAsync(
            "STDERR",
            process.StandardError,
            logWriter,
            logWriteGate);
        var host = new NativeWorkerProcessHost(process, logFilePath, logWriter,
            logWriteGate, stdoutPump, stderrPump, baseAddress);

        try
        {
            await WaitForLiveAsync(
                process,
                logFilePath,
                baseAddress,
                startupTimeout,
                cancellationToken).ConfigureAwait(false);
            host.AssertNoFatalMarkersInLogs();
            return host;
        }
        catch (Exception startupFailure)
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Native Worker 启动与清理均失败。", startupFailure, cleanupFailure);
            }

            throw;
        }
    }

    public async Task StopGracefullyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!_process.HasExited)
        {
            SendSigTerm(_process.Id);
            using var timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await _process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                await CompleteOutputAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    $"Native Worker 未在 {timeout} 内优雅退出。日志：{LogFilePath}");
            }
        }

        await CompleteOutputAsync().ConfigureAwait(false);
    }

    public void AssertNoFatalMarkersInLogs()
    {
        if (!File.Exists(LogFilePath))
        {
            return;
        }

        var content = NativeProcessLogReader.Read(LogFilePath);
        foreach (var marker in FatalLogMarkers)
        {
            Assert.IsFalse(
                content.Contains(marker, StringComparison.Ordinal),
                $"Native Worker 日志包含运行时故障标记 '{marker}'。日志：{LogFilePath}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? failure = null;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await CompleteOutputAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // 输出故障不能阻断句柄释放，也不能被随后的释放故障覆盖。
        try
        {
            await _logWriter.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception
                : new AggregateException("Native Worker 输出与日志释放均失败。", failure, exception);
        }
        finally
        {
            _logWriteGate.Dispose();
            _process.Dispose();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task CompleteOutputAsync()
    {
        await Task.WhenAll(_stdoutPump, _stderrPump).ConfigureAwait(false);
        await _logWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static Dictionary<string, string> BuildEnvironment(
        string repositoryRoot,
        DatabaseProvider provider,
        string connectionString,
        Uri baseAddress,
        string? filesRootPath,
        bool enableFilesUploadReconciliation,
        bool enableFilesCleanup,
        bool enableFilesReferenceClaimReconciliation,
        IReadOnlyDictionary<string, string?>? additionalSettings)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["ASPNETCORE_ENVIRONMENT"] = "Testing",
            ["ASPNETCORE_URLS"] = baseAddress.ToString().TrimEnd('/'),
            ["ASPNETCORE_CONTENTROOT"] = Path.Combine(
                repositoryRoot,
                "src",
                "Hosts",
                "Full.NET.Host.Worker"),
            [$"{DatabaseOptions.SectionName}__Provider"] = provider.ToString(),
            [$"{DatabaseOptions.SectionName}__ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}__CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}__MySqlGuidStorageMode"] = "Binary16",
            ["Messaging__Worker__Mode"] = "LegacyPolling",
            ["OutboxWorker__PollMilliseconds"] = "100",
            ["OutboxWorker__MaximumIdlePollMilliseconds"] = "100",
            ["Jobs__Worker__PollMilliseconds"] = "100",
            ["Jobs__Worker__BacklogSampleSeconds"] = "5",
            ["Realtime__Enabled"] = "false",
            ["Files__UploadReconciliation__Enabled"] =
                enableFilesUploadReconciliation.ToString(),
            ["Files__Cleanup__Enabled"] = enableFilesCleanup.ToString(),
            ["Files__ReferenceClaimReconciliation__Enabled"] =
                enableFilesReferenceClaimReconciliation.ToString(),
            ["Files__Local__RootPath"] = filesRootPath ?? Path.Combine(
                Path.GetTempPath(),
                "fullnet-worker-native-aot",
                Guid.NewGuid().ToString("N")),
            ["DataProtection__KeyRingPath"] = Path.Combine(
                Path.GetTempPath(),
                "fullnet-worker-native-aot-keys",
                Guid.NewGuid().ToString("N")),
        };
        if (enableFilesUploadReconciliation)
        {
            environment["Files__UploadReconciliation__MinimumAgeSeconds"] = "30";
            environment["Files__UploadReconciliation__PollSeconds"] = "5";
            environment["Files__UploadReconciliation__BatchSize"] = "10";
            environment["Files__UploadReconciliation__MaxBatchesPerRun"] = "1";
        }

        if (enableFilesCleanup)
        {
            environment["Files__Cleanup__PollSeconds"] = "5";
            environment["Files__Cleanup__BatchSize"] = "10";
            environment["Files__Cleanup__MaxBatchesPerRun"] = "1";
        }

        if (enableFilesReferenceClaimReconciliation)
        {
            environment["Files__ReferenceClaimReconciliation__PollSeconds"] = "5";
            environment["Files__ReferenceClaimReconciliation__BatchSize"] = "10";
            environment["Files__ReferenceClaimReconciliation__MaxBatchesPerRun"] = "1";
            environment["Files__ReferenceClaimReconciliation__MinimumAgeSeconds"] = "30";
            environment["Files__ReferenceClaimReconciliation__ReleaseGraceSeconds"] = "60";
        }

        if (additionalSettings is not null)
        {
            foreach (var setting in additionalSettings)
            {
                if (setting.Value is not null)
                {
                    environment[setting.Key.Replace(":", "__", StringComparison.Ordinal)] = setting.Value;
                }
            }
        }

        return environment;
    }

    private static async Task PumpStreamAsync(
        string source,
        TextReader reader,
        TextWriter writer,
        SemaphoreSlim writeGate)
    {
        while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false)
            is { } line)
        {
            await writeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync($"{source}: {line}").ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }
        }
    }

    private static async Task WaitForLiveAsync(
        Process process,
        string logFilePath,
        Uri baseAddress,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Native Worker 在启动前退出（代码 {process.ExitCode}）。日志：{logFilePath}\n{ReadLogTail(logFilePath)}");
            }

            try
            {
                using var response = await client.GetAsync(
                    new Uri(baseAddress, "/health/live"),
                    cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Native Worker 未在 {timeout} 内进入存活状态。日志：{logFilePath}\n{ReadLogTail(logFilePath)}");
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ReadLogTail(string logFilePath, int maxChars = 4_000)
    {
        if (!File.Exists(logFilePath))
        {
            return string.Empty;
        }

        var content = NativeProcessLogReader.Read(logFilePath);
        return content.Length <= maxChars ? content : content[^maxChars..];
    }

    private static void SendSigTerm(int processId)
    {
        using var signal = Process.Start(new ProcessStartInfo
        {
            FileName = "kill",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-TERM", processId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        }) ?? throw new InvalidOperationException("无法启动 kill 发送 SIGTERM。");
        if (!signal.WaitForExit(5_000))
        {
            signal.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"向 Native Worker {processId} 发送 SIGTERM 的 kill 进程未在 5 秒内退出。");
        }

        if (signal.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"无法向 Native Worker {processId} 发送 SIGTERM：{signal.StandardError.ReadToEnd()}");
        }
    }
}
