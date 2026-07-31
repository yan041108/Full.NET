using System.Diagnostics.Metrics;
using Full.NET.Modules.Jobs.Execution;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
[DoNotParallelize]
public sealed class JobsBacklogTelemetryTests
{
    [TestMethod]
    public void RecordBacklog_RecordsDepthAndAgesWithoutTags()
    {
        var observedAtUtc =
            new DateTimeOffset(2026, 7, 30, 1, 2, 3, TimeSpan.Zero);
        using var capture = new JobsBacklogMetricCapture();

        JobsTelemetry.RecordBacklog(
            new JobsBacklogSnapshot(
                2,
                observedAtUtc.AddSeconds(-90),
                3,
                observedAtUtc.AddSeconds(-120)),
            observedAtUtc);

        CollectionAssert.Contains(
            capture.Measurements,
            ("fullnet.jobs.backlog.executions", 2d, 0));
        CollectionAssert.Contains(
            capture.Measurements,
            ("fullnet.jobs.backlog.oldest_age", 90d, 0));
        CollectionAssert.Contains(
            capture.Measurements,
            ("fullnet.jobs.retry.due", 3d, 0));
        CollectionAssert.Contains(
            capture.Measurements,
            ("fullnet.jobs.retry.oldest_due_age", 120d, 0));
    }

    [TestMethod]
    public void RecordBacklog_UsesZeroForMissingOrFutureAges()
    {
        var observedAtUtc =
            new DateTimeOffset(2026, 7, 30, 1, 2, 3, TimeSpan.Zero);
        using var capture = new JobsBacklogMetricCapture();

        JobsTelemetry.RecordBacklog(
            new JobsBacklogSnapshot(
                0,
                null,
                1,
                observedAtUtc.AddSeconds(30)),
            observedAtUtc);

        CollectionAssert.Contains(
            capture.Measurements,
            ("fullnet.jobs.backlog.oldest_age", 0d, 0));
        CollectionAssert.Contains(
            capture.Measurements,
            ("fullnet.jobs.retry.oldest_due_age", 0d, 0));
    }

    private sealed class JobsBacklogMetricCapture : IDisposable
    {
        private readonly MeterListener _listener;

        public JobsBacklogMetricCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == JobsTelemetry.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, _) =>
                    Measurements.Add((
                        instrument.Name,
                        measurement,
                        tags.Length)));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, measurement, tags, _) =>
                    Measurements.Add((
                        instrument.Name,
                        measurement,
                        tags.Length)));
            _listener.Start();
        }

        public List<(string Name, double Value, int TagCount)> Measurements
        {
            get;
        } = [];

        public void Dispose() => _listener.Dispose();
    }
}
