using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 启动已发布的 Native Host.Api 进程，捕获日志并在退出时可靠清理。
/// </summary>
internal sealed class NativeApiProcessHost : IAsyncDisposable
{
    private static readonly Regex ListeningUrlRegex = new(
        @"Now listening on:\s*(?<url>https?://[^\s\}""]+)|""address""\s*:\s*""(?<url>https?://[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] FatalLogMarkers =
    [
        "MissingMethodException",
        "TypeInitializationException",
        "JsonSerializerIsReflectionDisabled",
        "IL3050",
        "IL2026",
    ];

    private readonly string _logFilePath;
    private readonly Process _process;
    private readonly StreamWriter _logWriter;
    private readonly CancellationTokenSource _logPumpCancellation;
    private readonly SemaphoreSlim _logWriteGate;
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private bool _disposed;

    private NativeApiProcessHost(
        Process process,
        string logFilePath,
        StreamWriter logWriter,
        SemaphoreSlim logWriteGate,
        CancellationTokenSource logPumpCancellation,
        Task stdoutPump,
        Task stderrPump,
        Uri baseAddress)
    {
        _process = process;
        _logFilePath = logFilePath;
        _logWriter = logWriter;
        _logWriteGate = logWriteGate;
        _logPumpCancellation = logPumpCancellation;
        _stdoutPump = stdoutPump;
        _stderrPump = stderrPump;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public string LogFilePath => _logFilePath;

    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    public static async Task<NativeApiProcessHost> StartAsync(
        NativeApiArtifact artifact,
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?> settings,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken = default)
    {
        var listenPort = GetFreeTcpPort();
        var baseAddress = new Uri($"http://127.0.0.1:{listenPort}/");
        var contentRoot = Path.Combine(
            artifact.RepositoryRoot,
            "src",
            "Hosts",
            "Full.NET.Host.Api");
        var logDirectory = Path.Combine(
            artifact.RepositoryRoot,
            "artifacts",
            "native-aot",
            "linux-x64",
            "test-logs");
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(
            logDirectory,
            $"fullnet-native-aot-{Guid.NewGuid():N}.log");

        var environment = BuildEnvironment(
            provider,
            connectionString,
            settings,
            baseAddress,
            contentRoot);

        var startInfo = new ProcessStartInfo
        {
            FileName = artifact.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = artifact.PublishDirectory,
        };
        foreach (var pair in environment)
        {
            if (pair.Value is not null)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        var logWriter = new StreamWriter(
            new FileStream(
                logFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        var logPumpCancellation = new CancellationTokenSource();
        var logWriteGate = new SemaphoreSlim(1, 1);
        Process process;
        try
        {
            // 先取得日志资源，再启动进程，避免文件创建失败留下无人接管的子进程。
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 Native Host.Api 进程。");
        }
        catch
        {
            await logWriter.DisposeAsync().ConfigureAwait(false);
            logWriteGate.Dispose();
            logPumpCancellation.Dispose();
            throw;
        }

        var stdoutPump = PumpStreamAsync(
            process.StandardOutput,
            logWriter,
            logWriteGate,
            logPumpCancellation.Token);
        var stderrPump = PumpStreamAsync(
            process.StandardError,
            logWriter,
            logWriteGate,
            logPumpCancellation.Token);
        var host = new NativeApiProcessHost(process, logFilePath, logWriter, logWriteGate,
            logPumpCancellation, stdoutPump, stderrPump, baseAddress);

        try
        {
            await WaitForListeningAsync(
                process,
                logFilePath,
                baseAddress,
                startupTimeout,
                cancellationToken).ConfigureAwait(false);
            AssertNoFatalMarkersInLog(logFilePath);
            return host;
        }
        catch (Exception startupFailure)
        {
            try
            {
                // 启动失败同样等待退出与 EOF；调用方取消不能丢弃已经写入管道的诊断。
                await host.CloseResourcesAsync(gracefulShutdown: false).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Native Host.Api 启动与清理均失败。", startupFailure, cleanupFailure);
            }

            throw;
        }
    }

    public HttpClient CreateClient(string hostHeader = "localhost")
    {
        // 与 FullNetApiFactory 一致：OIDC 授权码流返回外部 redirect_uri，禁止自动跟跳。
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = BaseAddress,
            Timeout = NativeAotTestTimeouts.HttpClient,
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Host", hostHeader);
        return client;
    }

    public async Task StopGracefullyAsync(CancellationToken cancellationToken = default)
    {
        if (!_process.HasExited)
        {
            if (OperatingSystem.IsLinux())
            {
                TrySendSigTerm(_process.Id);
                using var registration = cancellationToken.Register(() =>
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                });
                await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // 进程退出只关闭写入端；断言前仍须读取管道缓冲中的最后诊断。
        await DrainLogOutputAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>等待输出泵读到 EOF 并刷新日志，不以取消读取代替排空。</summary>
    private async Task DrainLogOutputAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(_stdoutPump, _stderrPump).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        await _logWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void AssertNoFatalMarkersInLogs() => AssertNoFatalMarkersInLog(_logFilePath);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CloseResourcesAsync(gracefulShutdown: true).ConfigureAwait(false);
        AssertNoFatalMarkersInLog(_logFilePath);
    }

    /// <summary>等待退出与输出完成，并在失败时仍释放本实例拥有的全部资源。</summary>
    private async Task CloseResourcesAsync(bool gracefulShutdown)
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
                if (gracefulShutdown && OperatingSystem.IsLinux())
                {
                    TrySendSigTerm(_process.Id);
                    if (!_process.WaitForExit(15_000))
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
                else
                {
                    _process.Kill(entireProcessTree: true);
                }

                await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            // 宿主退出后先排空 EOF；提前取消会丢掉停机错误并让断言误通过。
            await DrainLogOutputAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await _logWriter.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception
                : new AggregateException("Native Host.Api 输出与日志释放均失败。", failure, exception);
        }
        finally
        {
            _logPumpCancellation.Cancel();
            _logPumpCancellation.Dispose();
            _logWriteGate.Dispose();
            _process.Dispose();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Dictionary<string, string?> BuildEnvironment(
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?> settings,
        Uri baseAddress,
        string contentRoot)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["ASPNETCORE_ENVIRONMENT"] = "Testing",
            ["ASPNETCORE_URLS"] = baseAddress.ToString().TrimEnd('/'),
            ["ASPNETCORE_CONTENTROOT"] = contentRoot,
            [$"{DatabaseOptions.SectionName}__Provider"] = provider.ToString(),
            [$"{DatabaseOptions.SectionName}__ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}__CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}__MySqlGuidStorageMode"] = "Binary16",
            ["Identity__AllowDevelopmentEphemeralSigningKey"] = "true",
            ["Identity__RequireSecureCookies"] = "false",
            ["Identity__EnableRemoteSuperAdministratorManagement"] = "true",
            ["Identity__LoginRateLimitPermitLimitPerMinute"] = "1000",
            ["Identity__AllowedOrigins__0"] = "http://localhost",
            ["Tenancy__HostDomains__0"] = "localhost",
            ["Realtime__AllowSharedRedisInDevelopment"] = "true",
            ["Files__Local__RootPath"] = Path.Combine(
                Path.GetTempPath(),
                "fullnet-files-native-aot",
                Guid.NewGuid().ToString("N")),
        };

        foreach (var pair in settings)
        {
            environment[ToEnvironmentKey(pair.Key)] = pair.Value;
        }

        return environment;
    }

    private static string ToEnvironmentKey(string configurationKey) =>
        configurationKey.Replace(":", "__", StringComparison.Ordinal);

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task PumpStreamAsync(
        TextReader reader,
        TextWriter writer,
        SemaphoreSlim writeGate,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            // stdout/stderr 可同时完成读取，StreamWriter 的异步写入必须由同一门闩串行化。
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }
        }
    }

    private static async Task WaitForListeningAsync(
        Process process,
        string logFilePath,
        Uri expectedBaseAddress,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3),
        };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                var exitLogTail = await ReadLogTailAsync(logFilePath, cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Native Host.Api 在启动完成前退出（代码 {process.ExitCode}）。日志：{logFilePath}\n{exitLogTail}");
            }

            try
            {
                using var response = await httpClient.GetAsync(
                    new Uri(expectedBaseAddress, "/health/live"),
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

            if (File.Exists(logFilePath))
            {
                var content = await NativeProcessLogReader.ReadAsync(logFilePath, cancellationToken)
                    .ConfigureAwait(false);
                if (ListeningUrlRegex.IsMatch(content))
                {
                    // 结构化 Serilog 可能已写出监听地址，但 /health/live 仍不可达时继续轮询。
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        var logTail = await ReadLogTailAsync(logFilePath, cancellationToken)
            .ConfigureAwait(false);
        throw new TimeoutException(
            $"Native Host.Api 未在 {timeout} 内进入可服务状态。日志：{logFilePath}\n{logTail}");
    }

    private static async Task<string> ReadLogTailAsync(
        string logFilePath,
        CancellationToken cancellationToken,
        int maxChars = 4_000)
    {
        if (!File.Exists(logFilePath))
        {
            return string.Empty;
        }

        var content = await NativeProcessLogReader.ReadAsync(logFilePath, cancellationToken)
            .ConfigureAwait(false);
        var focused = TryExtractJsonMetadataFailureSnippet(content);
        if (!string.IsNullOrEmpty(focused))
        {
            return focused.Length <= maxChars
                ? focused
                : focused[^maxChars..];
        }

        if (content.Length <= maxChars)
        {
            return content;
        }

        return content[^maxChars..];
    }

    /// <summary>
    /// 从完整日志中提取最近一次 JSON 源生成元数据失败片段，避免 4KB 尾截断丢掉类型名。
    /// </summary>
    private static string? TryExtractJsonMetadataFailureSnippet(string content)
    {
        const string typeMarker = "deserialization of type '";
        var typeIndex = content.LastIndexOf(typeMarker, StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            return null;
        }

        var windowStart = Math.Max(0, typeIndex - 256);
        var windowEnd = Math.Min(content.Length, typeIndex + 6_000);
        return content[windowStart..windowEnd];
    }

    private static void TrySendSigTerm(int processId)
    {
        try
        {
            using var killProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "kill",
                Arguments = $"-TERM {processId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            killProcess?.WaitForExit(5_000);
        }
        catch
        {
            // 回退到强制终止由调用方处理。
        }
    }

    private static void AssertNoFatalMarkersInLog(string logFilePath)
    {
        if (!File.Exists(logFilePath))
        {
            return;
        }

        var content = NativeProcessLogReader.Read(logFilePath);
        foreach (var marker in FatalLogMarkers)
        {
            if (content.Contains(marker, StringComparison.Ordinal))
            {
                Assert.Fail(
                    $"Native Host.Api 日志包含运行时故障标记 '{marker}'。日志：{logFilePath}");
            }
        }
    }
}
