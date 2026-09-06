using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;

/// <summary>从当前进程采集跨平台可得的运行时指标，并显式标记不可用项。</summary>
internal sealed class ServerRuntimeReader
{
    private readonly IClock _clock;
    private readonly Func<Process> _processFactory;

    /// <summary>创建默认读取器，允许测试注入进程工厂以隔离平台差异。</summary>
    /// <param name="clock">系统时钟，用于计算运行时长与 CPU 采样窗口。</param>
    /// <param name="processFactory">可选进程工厂；缺省时绑定当前进程。</param>
    public ServerRuntimeReader(
        IClock clock,
        Func<Process>? processFactory = null)
    {
        _clock = clock;
        _processFactory = processFactory ?? (() => Process.GetCurrentProcess());
    }

    /// <summary>采集当前进程的运行时快照，不读取环境变量或连接串。</summary>
    /// <param name="instanceKey">实例稳定标识。</param>
    /// <param name="displayName">实例展示名称。</param>
    /// <param name="hostRole">宿主角色。</param>
    /// <param name="cancellationToken">取消令牌；CPU 采样窗口会短暂阻塞。</param>
    /// <returns>只读运行时快照。</returns>
    public async Task<ServerRuntimeSnapshot> CaptureAsync(
        string instanceKey,
        string displayName,
        string hostRole,
        CancellationToken cancellationToken)
    {
        using var process = _processFactory();
        var capturedAt = _clock.UtcNow;
        var startedAt = SafeProcessStartTime(process, capturedAt);
        var uptimeSeconds = Math.Max(0, (long)(capturedAt - startedAt).TotalSeconds);
        var metrics = new List<ServerRuntimeMetric>
        {
            BuildProcessorCountMetric(),
            await BuildCpuUsageMetricAsync(process, cancellationToken).ConfigureAwait(false),
            BuildMemoryMetric(
                "working_set_bytes",
                "工作集内存",
                () => process.WorkingSet64),
            BuildMemoryMetric(
                "private_memory_bytes",
                "私有内存",
                () => process.PrivateMemorySize64),
            BuildGcHeapMetric(),
        };

        return new ServerRuntimeSnapshot(
            instanceKey,
            displayName,
            hostRole,
            Environment.MachineName,
            process.Id,
            RuntimeInformation.FrameworkDescription,
            ResolveApplicationVersion(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            startedAt,
            capturedAt,
            uptimeSeconds,
            metrics);
    }

    private static string ResolveApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static DateTimeOffset SafeProcessStartTime(
        Process process,
        DateTimeOffset capturedAt)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (PlatformNotSupportedException)
        {
            return capturedAt;
        }
        catch (InvalidOperationException)
        {
            return capturedAt;
        }
    }

    private static ServerRuntimeMetric BuildProcessorCountMetric() =>
        new(
            "processor_count",
            "逻辑处理器数",
            Environment.ProcessorCount,
            null,
            "count",
            ServerRuntimeMetricAvailability.Available,
            null);

    private static ServerRuntimeMetric BuildGcHeapMetric()
    {
        try
        {
            return new ServerRuntimeMetric(
                "gc_heap_bytes",
                "GC 堆内存",
                GC.GetTotalMemory(forceFullCollection: false),
                null,
                "bytes",
                ServerRuntimeMetricAvailability.Available,
                null);
        }
        catch (Exception)
        {
            return UnavailableMetric(
                "gc_heap_bytes",
                "GC 堆内存",
                "bytes",
                "当前运行时无法读取 GC 堆大小。");
        }
    }

    private static ServerRuntimeMetric BuildMemoryMetric(
        string key,
        string label,
        Func<long> readValue)
    {
        try
        {
            return new ServerRuntimeMetric(
                key,
                label,
                readValue(),
                null,
                "bytes",
                ServerRuntimeMetricAvailability.Available,
                null);
        }
        catch (PlatformNotSupportedException)
        {
            return UnavailableMetric(
                key,
                label,
                "bytes",
                "当前平台不支持读取该内存指标。",
                ServerRuntimeMetricAvailability.NotSupportedOnPlatform);
        }
        catch (InvalidOperationException)
        {
            return UnavailableMetric(
                key,
                label,
                "bytes",
                "进程已退出或句柄不可用。");
        }
    }

    private async Task<ServerRuntimeMetric> BuildCpuUsageMetricAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()
            && !OperatingSystem.IsLinux()
            && !OperatingSystem.IsMacOS())
        {
            return UnavailableMetric(
                "cpu_usage_percent",
                "CPU 使用率",
                "percent",
                "当前操作系统未纳入 CPU 采样支持范围。",
                ServerRuntimeMetricAvailability.NotSupportedOnPlatform);
        }

        try
        {
            var startedCpu = process.TotalProcessorTime;
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken)
                .ConfigureAwait(false);
            process.Refresh();
            var endedCpu = process.TotalProcessorTime;
            var elapsedMs = 120d;
            var cpuMs = (endedCpu - startedCpu).TotalMilliseconds;
            var processorCount = Math.Max(1, Environment.ProcessorCount);
            var usagePercent = Math.Round(cpuMs / (elapsedMs * processorCount) * 100d, 2);
            usagePercent = Math.Clamp(usagePercent, 0d, 100d);

            return new ServerRuntimeMetric(
                "cpu_usage_percent",
                "CPU 使用率",
                null,
                usagePercent,
                "percent",
                ServerRuntimeMetricAvailability.Available,
                null);
        }
        catch (PlatformNotSupportedException)
        {
            return UnavailableMetric(
                "cpu_usage_percent",
                "CPU 使用率",
                "percent",
                "当前平台不支持进程 CPU 时间采样。",
                ServerRuntimeMetricAvailability.NotSupportedOnPlatform);
        }
        catch (InvalidOperationException)
        {
            return UnavailableMetric(
                "cpu_usage_percent",
                "CPU 使用率",
                "percent",
                "进程已退出或句柄不可用。");
        }
    }

    private static ServerRuntimeMetric UnavailableMetric(
        string key,
        string label,
        string unit,
        string reason,
        string availability = ServerRuntimeMetricAvailability.Unavailable) =>
        new(key, label, null, null, unit, availability, reason);
}
