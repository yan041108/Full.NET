using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using Full.NET.Data.Abstractions;

namespace Full.NET.Benchmarks.MixedLoad;

public sealed record MixedLoadConnectionPoolSnapshot(
    int ConfiguredMaximumConnections,
    double? PeakActiveConnections,
    double? PeakIdleConnections,
    double? PeakPooledConnections,
    double? PeakPendingRequests,
    double? PeakStasisConnections,
    long? ConnectionTimeouts,
    long? ReclaimedConnections,
    MixedLoadLatencyStatistics? WaitDuration,
    IReadOnlyList<string> PublishedInstruments,
    int MaximumSafeActiveConnections,
    bool CapacityHeadroomPassed,
    string ObservationMode,
    bool EvidenceComplete,
    string? EvidenceError);

public interface IMixedLoadConnectionPoolTelemetry : IDisposable
{
    void Reset();

    MixedLoadConnectionPoolSnapshot Snapshot();
}

public static class MixedLoadConnectionPoolTelemetry
{
    public const int MaximumPoolSize = 100;
    public const double MaximumActivePoolRatio = 0.90d;

    public static IMixedLoadConnectionPoolTelemetry Create(
        DatabaseProvider provider,
        string poolName) =>
        provider switch
        {
            DatabaseProvider.SqlServer =>
                new SqlClientConnectionPoolTelemetry(MaximumPoolSize),
            DatabaseProvider.MySql =>
                new MySqlConnectionPoolTelemetry(poolName),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的连接池指标 Provider。"),
        };
}

public sealed class MySqlConnectionPoolTelemetry :
    IMixedLoadConnectionPoolTelemetry
{
    private const string MeterName = "MySqlConnector";
    private const string Usage = "db.client.connections.usage";
    private const string Pending = "db.client.connections.pending_requests";
    private const string Timeouts = "db.client.connections.timeouts";
    private const string WaitTime = "db.client.connections.wait_time";
    private const string Maximum = "db.client.connections.max";
    private readonly object _sync = new();
    private readonly MeterListener _listener = new();
    private readonly HashSet<string> _published = new(StringComparer.Ordinal);
    private readonly List<double> _waitMilliseconds = [];
    private readonly string _poolName;
    private double _active;
    private double _idle;
    private double _pending;
    private double _maximum;
    private double _peakActive;
    private double _peakIdle;
    private double _peakPending;
    private long _timeouts;

    public MySqlConnectionPoolTelemetry(string poolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolName);
        _poolName = poolName;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (!string.Equals(
                    instrument.Meter.Name,
                    MeterName,
                    StringComparison.Ordinal))
            {
                return;
            }

            lock (_sync)
            {
                _published.Add(instrument.Name);
            }

            listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _listener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, state) =>
                OnLongMeasurement(instrument, measurement, tags, state));
        _listener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);
        _listener.Start();
    }

    public void Reset()
    {
        _listener.RecordObservableInstruments();
        lock (_sync)
        {
            _peakActive = _active;
            _peakIdle = _idle;
            _peakPending = _pending;
            _timeouts = 0;
            _waitMilliseconds.Clear();
        }
    }

    public MixedLoadConnectionPoolSnapshot Snapshot()
    {
        _listener.RecordObservableInstruments();
        lock (_sync)
        {
            var required = new[] { Usage, Pending, Timeouts, WaitTime, Maximum };
            var configuredMaximum = (int)Math.Round(
                _maximum > 0
                    ? _maximum
                    : MixedLoadConnectionPoolTelemetry.MaximumPoolSize);
            var safeMaximum = CalculateSafeMaximum(configuredMaximum);
            var headroomPassed = _peakActive <= safeMaximum;
            var complete = required.All(_published.Contains)
                && _maximum > 0
                && headroomPassed;
            return new MixedLoadConnectionPoolSnapshot(
                configuredMaximum,
                _peakActive,
                _peakIdle,
                _peakActive + _peakIdle,
                _peakPending,
                null,
                _timeouts,
                null,
                _waitMilliseconds.Count == 0
                    ? null
                    : MixedLoadLatencyStatistics.Calculate(_waitMilliseconds),
                _published.Order(StringComparer.Ordinal).ToArray(),
                safeMaximum,
                headroomPassed,
                "MySqlConnector Meter：直接观测 active/idle/pending/timeout/wait。",
                complete,
                complete
                    ? null
                    : headroomPassed
                        ? "MySqlConnector 连接池指标未完整发布。"
                        : "MySqlConnector 连接池 active 峰值超过安全余量。");
        }
    }

    public void Dispose() => _listener.Dispose();

    private void OnLongMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state) =>
        RecordMeasurement(instrument, measurement, tags);

    private void OnDoubleMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state) =>
        RecordMeasurement(instrument, measurement, tags);

    private void RecordMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!string.Equals(
                GetTag(tags, "pool.name"),
                _poolName,
                StringComparison.Ordinal))
        {
            return;
        }

        lock (_sync)
        {
            switch (instrument.Name)
            {
                case Usage:
                    if (string.Equals(
                            GetTag(tags, "state"),
                            "used",
                            StringComparison.Ordinal))
                    {
                        _active += measurement;
                        _peakActive = Math.Max(_peakActive, _active);
                    }
                    else if (string.Equals(
                                 GetTag(tags, "state"),
                                 "idle",
                                 StringComparison.Ordinal))
                    {
                        _idle += measurement;
                        _peakIdle = Math.Max(_peakIdle, _idle);
                    }

                    break;
                case Pending:
                    _pending += measurement;
                    _peakPending = Math.Max(_peakPending, _pending);
                    break;
                case Timeouts:
                    _timeouts += checked((long)measurement);
                    break;
                case WaitTime:
                    _waitMilliseconds.Add(measurement * 1000d);
                    break;
                case Maximum:
                    _maximum = Math.Max(_maximum, measurement);
                    break;
            }
        }
    }

    private static string? GetTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string key)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return Convert.ToString(
                    tag.Value,
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static int CalculateSafeMaximum(int configuredMaximum) =>
        (int)Math.Floor(
            configuredMaximum
            * MixedLoadConnectionPoolTelemetry.MaximumActivePoolRatio);
}

public sealed class SqlClientConnectionPoolTelemetry :
    EventListener,
    IMixedLoadConnectionPoolTelemetry
{
    private const string EventSourceName = "Microsoft.Data.SqlClient.EventSource";
    private const string Active = "number-of-active-connections";
    private const string Pooled = "number-of-pooled-connections";
    private const string Free = "number-of-free-connections";
    private const string Stasis = "number-of-stasis-connections";
    private const string Reclaimed = "number-of-reclaimed-connections";
    private readonly object _sync = new();
    private readonly Dictionary<string, double> _latest =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _peaks =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _published = new(StringComparer.Ordinal);
    private readonly int _maximumPoolSize;
    private double _reclaimedBaseline;

    public SqlClientConnectionPoolTelemetry(int maximumPoolSize)
    {
        _maximumPoolSize = maximumPoolSize;
    }

    public void Reset()
    {
        lock (_sync)
        {
            _peaks.Clear();
            foreach (var pair in _latest)
            {
                _peaks[pair.Key] = pair.Value;
            }

            _reclaimedBaseline = _latest.GetValueOrDefault(Reclaimed);
        }
    }

    public MixedLoadConnectionPoolSnapshot Snapshot()
    {
        lock (_sync)
        {
            var required = new[] { Active, Pooled, Free, Stasis };
            var peakActive = GetPeak(Active);
            var safeMaximum = (int)Math.Floor(
                _maximumPoolSize
                * MixedLoadConnectionPoolTelemetry.MaximumActivePoolRatio);
            var headroomPassed = peakActive.HasValue
                && peakActive.Value <= safeMaximum;
            var complete = required.All(_published.Contains)
                && headroomPassed;
            return new MixedLoadConnectionPoolSnapshot(
                _maximumPoolSize,
                peakActive,
                GetPeak(Free),
                GetPeak(Pooled),
                null,
                GetPeak(Stasis),
                null,
                _published.Contains(Reclaimed)
                    ? (long)Math.Max(
                        0d,
                        _latest.GetValueOrDefault(Reclaimed) - _reclaimedBaseline)
                    : null,
                null,
                _published.Order(StringComparer.Ordinal).ToArray(),
                safeMaximum,
                headroomPassed,
                "SqlClient EventCounters：直接观测 active/pooled/free/stasis；"
                + "以 90% active 安全余量和零 Dapper 失败替代未公开的 pending/timeout。",
                complete,
                complete
                    ? null
                    : headroomPassed
                        ? "SqlClient EventCounters 未完整发布连接池指标。"
                        : "SqlClient 连接池 active 峰值超过安全余量或缺少峰值。");
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (!string.Equals(
                eventSource.Name,
                EventSourceName,
                StringComparison.Ordinal))
        {
            return;
        }

        EnableEvents(
            eventSource,
            EventLevel.Informational,
            EventKeywords.None,
            new Dictionary<string, string?>
            {
                ["EventCounterIntervalSec"] = "1",
            });
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var counters = eventData.Payload?
            .OfType<IDictionary<string, object?>>()
            .FirstOrDefault(values => values.ContainsKey("Name"));
        if (counters is null
            || !counters.TryGetValue("Name", out var rawName)
            || rawName is not string name
            || !counters.TryGetValue("Mean", out var rawValue)
            || rawValue is null)
        {
            return;
        }

        var value = Convert.ToDouble(
            rawValue,
            System.Globalization.CultureInfo.InvariantCulture);
        lock (_sync)
        {
            _published.Add(name);
            _latest[name] = value;
            _peaks[name] = Math.Max(_peaks.GetValueOrDefault(name), value);
        }
    }

    private double? GetPeak(string name) =>
        _published.Contains(name)
            ? _peaks.GetValueOrDefault(name)
            : null;
}
