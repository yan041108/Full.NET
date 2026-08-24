using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
    private readonly CancellationTokenSource _logPumpCancellation = new();
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private bool _disposed;

    private NativeApiProcessHost(
        Process process,
        string logFilePath,
        StreamWriter logWriter,
        CancellationTokenSource logPumpCancellation,
        Task stdoutPump,
        Task stderrPump,
        Uri baseAddress)
    {
        _process = process;
        _logFilePath = logFilePath;
        _logWriter = logWriter;
        _logPumpCancellation = logPumpCancellation;
        _stdoutPump = stdoutPump;
        _stderrPump = stderrPump;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public string LogFilePath => _logFilePath;

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
        var logFilePath = Path.Combine(
            Path.GetTempPath(),
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

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Native Host.Api 进程。");
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
        var stdoutPump = PumpStreamAsync(
            process.StandardOutput,
            logWriter,
            logPumpCancellation.Token);
        var stderrPump = PumpStreamAsync(
            process.StandardError,
            logWriter,
            logPumpCancellation.Token);

        try
        {
            await WaitForListeningAsync(
                process,
                logFilePath,
                baseAddress,
                startupTimeout,
                cancellationToken).ConfigureAwait(false);
            AssertNoFatalMarkersInLog(logFilePath);
            return new NativeApiProcessHost(
                process,
                logFilePath,
                logWriter,
                logPumpCancellation,
                stdoutPump,
                stderrPump,
                baseAddress);
        }
        catch
        {
            logPumpCancellation.Cancel();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await logWriter.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public HttpClient CreateClient(string hostHeader = "localhost")
    {
        var client = new HttpClient
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(60),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Host", hostHeader);
        return client;
    }

    public async Task StopGracefullyAsync(CancellationToken cancellationToken = default)
    {
        if (_process.HasExited)
        {
            return;
        }

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
            return;
        }

        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void AssertNoFatalMarkersInLogs() => AssertNoFatalMarkersInLog(_logFilePath);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logPumpCancellation.Cancel();
        try
        {
            await Task.WhenAll(_stdoutPump, _stderrPump).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (!_process.HasExited)
        {
            if (OperatingSystem.IsLinux())
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
        }

        await _logWriter.DisposeAsync().ConfigureAwait(false);
        AssertNoFatalMarkersInLog(_logFilePath);
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
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            await writer.WriteLineAsync(line.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
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
                var content = await File.ReadAllTextAsync(logFilePath, cancellationToken)
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

        var content = await File.ReadAllTextAsync(logFilePath, cancellationToken)
            .ConfigureAwait(false);
        if (content.Length <= maxChars)
        {
            return content;
        }

        return content[^maxChars..];
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

        var content = File.ReadAllText(logFilePath);
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
