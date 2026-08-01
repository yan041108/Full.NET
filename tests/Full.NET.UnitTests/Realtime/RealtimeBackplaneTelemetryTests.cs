using System.Diagnostics.Metrics;
using Full.NET.Realtime.SignalR;
using Full.NET.Realtime.SignalR.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Realtime;

[TestClass]
[DoNotParallelize]
public sealed class RealtimeBackplaneTelemetryTests
{
    [TestMethod]
    public async Task Healthy_probe_records_ready_state_outcome_and_duration()
    {
        using var capture = new MetricCapture();
        var healthCheck = CreateHealthCheck(
            _ => Task.CompletedTask);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Healthy, result.Status);
        AssertOutcome(capture, "healthy", expectedState: 1);
    }

    [TestMethod]
    public async Task Internal_timeout_records_timeout_without_exposing_details()
    {
        using var capture = new MetricCapture();
        var healthCheck = CreateHealthCheck(
            _ => Task.FromException(
                new OperationCanceledException("Redis endpoint secret")));

        var degraded = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());
        Assert.AreEqual(HealthStatus.Degraded, degraded.Status);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
        Assert.AreEqual(
            "Realtime Redis Backplane 健康检查超时。",
            result.Description);
        Assert.IsNotNull(result.Description);
        Assert.DoesNotContain(
            "secret",
            result.Description,
            StringComparison.OrdinalIgnoreCase);
        AssertOutcome(capture, "timeout", expectedState: 0);
    }

    [TestMethod]
    public async Task Native_timeout_records_timeout_without_exposing_details()
    {
        using var capture = new MetricCapture();
        var healthCheck = CreateHealthCheck(
            _ => Task.FromException(
                new TimeoutException("Redis endpoint secret")));

        _ = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
        Assert.AreEqual(
            "Realtime Redis Backplane 健康检查超时。",
            result.Description);
        Assert.IsNotNull(result.Description);
        Assert.DoesNotContain(
            "secret",
            result.Description,
            StringComparison.OrdinalIgnoreCase);
        AssertOutcome(capture, "timeout", expectedState: 0);
    }

    [TestMethod]
    public async Task Probe_failure_records_failure_without_exposing_details()
    {
        using var capture = new MetricCapture();
        var healthCheck = CreateHealthCheck(
            _ => Task.FromException(
                new InvalidOperationException("Redis endpoint secret")));

        _ = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
        Assert.AreEqual(
            "Realtime Redis Backplane 健康检查失败。",
            result.Description);
        Assert.IsNotNull(result.Description);
        Assert.DoesNotContain(
            "secret",
            result.Description,
            StringComparison.OrdinalIgnoreCase);
        AssertOutcome(capture, "failure", expectedState: 0);
    }

    [TestMethod]
    public async Task Caller_cancellation_propagates_without_recording_a_false_failure()
    {
        using var capture = new MetricCapture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var healthCheck = CreateHealthCheck(
            token => Task.FromCanceled(token));

        _ = await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => healthCheck.CheckHealthAsync(
                new HealthCheckContext(),
                cancellation.Token));

        Assert.IsEmpty(capture.LongMeasurements);
        Assert.IsEmpty(capture.DoubleMeasurements);
    }

    [TestMethod]
    public async Task Metric_listener_failure_does_not_change_ready_result()
    {
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name ==
                    RealtimeBackplaneTelemetry.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, _, _) =>
                throw new InvalidOperationException(
                    "模拟指标消费者失败。"));
        listener.SetMeasurementEventCallback<double>(
            (_, _, _, _) =>
                throw new InvalidOperationException(
                    "模拟指标消费者失败。"));
        listener.Start();
        var healthCheck = CreateHealthCheck(
            _ => Task.CompletedTask);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Healthy, result.Status);
    }

    private static RealtimeBackplaneHealthCheck CreateHealthCheck(
        Func<CancellationToken, Task> pingAsync) =>
        new(
            Options.Create(new RealtimeOptions
            {
                RedisBackplaneConnectionString = "127.0.0.1:6379",
            }),
            new StubRealtimeBackplaneProbe(pingAsync));

    private static void AssertOutcome(
        MetricCapture capture,
        string outcome,
        long expectedState)
    {
        // 滞回探测会记录多次；断言取最后一次结果。
        var state = capture.LongMeasurements.Last(item =>
            item.Name ==
            "fullnet.realtime.backplane.readiness.state");
        Assert.AreEqual(expectedState, state.Value);
        Assert.IsEmpty(state.Tags);

        var checks = capture.LongMeasurements.Last(item =>
            item.Name ==
            "fullnet.realtime.backplane.readiness.checks");
        Assert.AreEqual(1L, checks.Value);
        AssertOutcomeTag(checks.Tags, outcome);

        var duration = capture.DoubleMeasurements.Last(item =>
            item.Name ==
            "fullnet.realtime.backplane.readiness.duration");
        Assert.IsGreaterThanOrEqualTo(0d, duration.Value);
        AssertOutcomeTag(duration.Tags, outcome);
    }

    private static void AssertOutcomeTag(
        KeyValuePair<string, object?>[] tags,
        string outcome)
    {
        Assert.HasCount(1, tags);
        Assert.AreEqual("outcome", tags[0].Key);
        Assert.AreEqual(outcome, tags[0].Value);
    }

    private sealed class StubRealtimeBackplaneProbe(
        Func<CancellationToken, Task> pingAsync)
        : IRealtimeBackplaneProbe
    {
        public Task PingAsync(
            string connectionString,
            CancellationToken cancellationToken)
        {
            _ = connectionString;
            return pingAsync(cancellationToken);
        }
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name ==
                    RealtimeBackplaneTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) =>
                    LongMeasurements.Add(
                        new LongMeasurement(
                            instrument.Name,
                            value,
                            tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) =>
                    DoubleMeasurements.Add(
                        new DoubleMeasurement(
                            instrument.Name,
                            value,
                            tags.ToArray())));
            _listener.Start();
        }

        public List<LongMeasurement> LongMeasurements { get; } = [];

        public List<DoubleMeasurement> DoubleMeasurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed record LongMeasurement(
        string Name,
        long Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed record DoubleMeasurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);
}
