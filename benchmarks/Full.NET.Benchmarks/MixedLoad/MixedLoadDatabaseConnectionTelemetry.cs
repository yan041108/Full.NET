using System.Diagnostics.Metrics;
using System.Globalization;

namespace Full.NET.Benchmarks.MixedLoad;

public sealed record MixedLoadDatabaseConnectionSnapshot(
    long Attempts,
    MixedLoadLatencyStatistics? WaitDuration,
    IReadOnlyDictionary<string, long> Outcomes,
    int CapturedSamples,
    long DroppedSamples,
    bool EvidenceComplete,
    string? EvidenceError);

/// <summary>
/// 捕获 Full.NET 数据会话边界的连接获取等待，补齐不同驱动池指标不对称的问题。
/// </summary>
public sealed class MixedLoadDatabaseConnectionTelemetry : IDisposable
{
    private const int DefaultSampleCapacity = 131_072;
    private const string MeterName = "fullnet.data.connection_pool";
    private const string WaitInstrumentName = "fullnet.db.connection.wait";
    private readonly MeterListener _listener = new();
    private readonly double[] _waitMilliseconds;
    private readonly string _provider;
    private long _attempts;
    private long _capturedSamples;
    private long _droppedSamples;
    private long _successes;
    private long _timeouts;
    private long _cancellations;
    private long _rejections;
    private long _failures;
    private long _unknown;

    public MixedLoadDatabaseConnectionTelemetry(
        string provider,
        int sampleCapacity = DefaultSampleCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCapacity);
        _provider = provider;
        // 在测量窗口开始前一次性分配，避免逐写入采样污染 allocated/write。
        _waitMilliseconds = new double[sampleCapacity];
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, MeterName, StringComparison.Ordinal)
                && string.Equals(
                    instrument.Name,
                    WaitInstrumentName,
                    StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.Start();
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _attempts, 0);
        Interlocked.Exchange(ref _capturedSamples, 0);
        Interlocked.Exchange(ref _droppedSamples, 0);
        Interlocked.Exchange(ref _successes, 0);
        Interlocked.Exchange(ref _timeouts, 0);
        Interlocked.Exchange(ref _cancellations, 0);
        Interlocked.Exchange(ref _rejections, 0);
        Interlocked.Exchange(ref _failures, 0);
        Interlocked.Exchange(ref _unknown, 0);
    }

    public MixedLoadDatabaseConnectionSnapshot Snapshot()
    {
        var captured = checked((int)Interlocked.Read(ref _capturedSamples));
        var dropped = Interlocked.Read(ref _droppedSamples);
        var waits = _waitMilliseconds.AsSpan(0, captured).ToArray();
        return new MixedLoadDatabaseConnectionSnapshot(
            Interlocked.Read(ref _attempts),
            waits.Length == 0
                ? null
                : MixedLoadLatencyStatistics.Calculate(waits),
            CreateOutcomes(),
            captured,
            dropped,
            dropped == 0,
            dropped == 0
                ? null
                : $"连接获取等待样本超过有界容量，已丢弃 {dropped} 个样本。");
    }

    public void Dispose() => _listener.Dispose();

    private void OnMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (!string.Equals(
                GetTag(tags, "provider"),
                _provider,
                StringComparison.Ordinal))
        {
            return;
        }

        var sampleIndex = Interlocked.Increment(ref _attempts) - 1;
        if (sampleIndex < _waitMilliseconds.Length)
        {
            _waitMilliseconds[checked((int)sampleIndex)] = measurement * 1000d;
            Interlocked.Increment(ref _capturedSamples);
        }
        else
        {
            Interlocked.Increment(ref _droppedSamples);
        }

        switch (GetTag(tags, "outcome"))
        {
            case "success":
                Interlocked.Increment(ref _successes);
                break;
            case "timeout":
                Interlocked.Increment(ref _timeouts);
                break;
            case "canceled":
                Interlocked.Increment(ref _cancellations);
                break;
            case "rejected":
                Interlocked.Increment(ref _rejections);
                break;
            case "failure":
                Interlocked.Increment(ref _failures);
                break;
            default:
                Interlocked.Increment(ref _unknown);
                break;
        }
    }

    private IReadOnlyDictionary<string, long> CreateOutcomes() =>
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["success"] = Interlocked.Read(ref _successes),
            ["timeout"] = Interlocked.Read(ref _timeouts),
            ["canceled"] = Interlocked.Read(ref _cancellations),
            ["rejected"] = Interlocked.Read(ref _rejections),
            ["failure"] = Interlocked.Read(ref _failures),
            ["unknown"] = Interlocked.Read(ref _unknown),
        };

    private static string? GetTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string key)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }
}
