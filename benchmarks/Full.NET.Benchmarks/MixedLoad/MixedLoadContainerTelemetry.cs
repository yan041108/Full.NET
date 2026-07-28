using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;

namespace Full.NET.Benchmarks.MixedLoad;

public sealed record MixedLoadContainerSnapshot(
    int SampleCount,
    double AverageCpuPercentOfHost,
    double PeakCpuPercentOfHost,
    ulong PeakMemoryBytes,
    bool EvidenceComplete,
    string? EvidenceError);

public sealed class MixedLoadContainerTelemetry : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private readonly object _sync = new();
    private readonly IDockerClient _client;
    private readonly string _containerId;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<(double CpuPercent, ulong MemoryBytes)> _samples = [];
    private Task? _samplingTask;
    private string? _error;
    private string? _lastIncompleteSample;

    public MixedLoadContainerTelemetry(string containerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        _containerId = containerId;
        _client = TestcontainersSettings.OS.DockerEndpointAuthConfig
            .GetDockerClientBuilder(Guid.NewGuid())
            .Build();
    }

    public void Start()
    {
        if (_samplingTask is not null)
        {
            throw new InvalidOperationException("容器资源采样不能重复启动。");
        }

        _samplingTask = SampleLoopAsync(_stopping.Token);
    }

    public async Task<MixedLoadContainerSnapshot> StopAsync()
    {
        if (_samplingTask is null)
        {
            throw new InvalidOperationException("容器资源采样尚未启动。");
        }

        await _stopping.CancelAsync();
        await _samplingTask;
        lock (_sync)
        {
            var complete = _samples.Count > 0 && _error is null;
            return new MixedLoadContainerSnapshot(
                _samples.Count,
                _samples.Count == 0
                    ? 0d
                    : _samples.Average(sample => sample.CpuPercent),
                _samples.Count == 0
                    ? 0d
                    : _samples.Max(sample => sample.CpuPercent),
                _samples.Count == 0
                    ? 0UL
                    : _samples.Max(sample => sample.MemoryBytes),
                complete,
                complete
                    ? null
                    : _error
                        ?? _lastIncompleteSample
                        ?? "数据库容器未产生资源样本。");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_samplingTask is not null && !_samplingTask.IsCompleted)
        {
            await _stopping.CancelAsync();
            await _samplingTask;
        }

        _stopping.Dispose();
        _client.Dispose();
    }

    public static double CalculateCpuPercentOfHost(
        ulong currentContainerUsage,
        ulong previousContainerUsage,
        ulong currentSystemUsage,
        ulong previousSystemUsage)
    {
        var containerDelta = currentContainerUsage >= previousContainerUsage
            ? currentContainerUsage - previousContainerUsage
            : 0UL;
        var systemDelta = currentSystemUsage >= previousSystemUsage
            ? currentSystemUsage - previousSystemUsage
            : 0UL;
        return systemDelta == 0
            ? 0d
            : (double)containerDelta / systemDelta * 100d;
    }

    private async Task SampleLoopAsync(CancellationToken cancellationToken)
    {
        ulong? previousContainerUsage = null;
        ulong? previousSystemUsage = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ContainerStatsResponse? response = null;
                await _client.Containers.GetContainerStatsAsync(
                    _containerId,
                    new ContainerStatsParameters
                    {
                        Stream = false,
                        OneShot = true,
                    },
                    new SynchronousProgress<ContainerStatsResponse>(
                        value => response = value),
                    cancellationToken);
                if (response is null)
                {
                    throw new InvalidOperationException(
                        "Docker Engine 未返回数据库容器资源样本。");
                }

                if (response.CPUStats?.CPUUsage is null
                    || response.CPUStats.SystemUsage is null
                    || response.MemoryStats?.Usage is null)
                {
                    lock (_sync)
                    {
                        _lastIncompleteSample =
                            "Docker Engine 返回的数据库容器资源样本不完整："
                            + $"cpu={response.CPUStats?.CPUUsage is not null}, "
                            + $"system={response.CPUStats?.SystemUsage}, "
                            + $"memory={response.MemoryStats?.Usage}。";
                    }
                }
                else
                {
                    var currentContainerUsage =
                        response.CPUStats.CPUUsage.TotalUsage;
                    var currentSystemUsage =
                        response.CPUStats.SystemUsage.Value;
                    if (previousContainerUsage.HasValue
                        && previousSystemUsage.HasValue
                        && currentSystemUsage > previousSystemUsage.Value)
                    {
                        var cpu = CalculateCpuPercentOfHost(
                            currentContainerUsage,
                            previousContainerUsage.Value,
                            currentSystemUsage,
                            previousSystemUsage.Value);
                        lock (_sync)
                        {
                            _samples.Add((
                                cpu,
                                response.MemoryStats.Usage.Value));
                        }
                    }

                    previousContainerUsage = currentContainerUsage;
                    previousSystemUsage = currentSystemUsage;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _error = $"{exception.GetType().Name}: {exception.Message}";
                }

                break;
            }

            try
            {
                await Task.Delay(SampleInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
