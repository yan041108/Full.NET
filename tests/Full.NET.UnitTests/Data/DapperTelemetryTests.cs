using System.Diagnostics.Metrics;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;

namespace Full.NET.UnitTests.Data;

[TestClass]
[DoNotParallelize]
public sealed class DapperTelemetryTests
{
    [TestMethod]
    public void RecordSucceeded_EmitsDurationAndExecutionWithLowCardinalityTags()
    {
        var measurements = new List<Measurement>();
        using var listener = CreateListener(measurements);

        DapperTelemetry.Record(
            "identity.find_refresh_session_by_id",
            DatabaseProvider.SqlServer,
            DapperOperation.QuerySingle,
            TimeSpan.FromMilliseconds(12.5),
            exception: null);

        Assert.IsTrue(measurements.Any(measurement =>
            measurement.Name == "fullnet.data.sql.duration"
            && measurement.Value == 12.5d
            && measurement.Tags["statement_name"]
                == "identity.find_refresh_session_by_id"
            && measurement.Tags["provider"] == "sql_server"
            && measurement.Tags["operation"] == "query_single"
            && measurement.Tags["outcome"] == "success"));
        Assert.IsTrue(measurements.Any(measurement =>
            measurement.Name == "fullnet.data.sql.executions"
            && measurement.Value == 1d));
        Assert.IsFalse(measurements.Any(measurement =>
            measurement.Tags.ContainsKey("sql")
            || measurement.Tags.ContainsKey("tenant_id")
            || measurement.Tags.ContainsKey("user_id")));
    }

    [TestMethod]
    public void RecordFailed_EmitsFailureAndCanceledOutcome()
    {
        var measurements = new List<Measurement>();
        using var listener = CreateListener(measurements);

        DapperTelemetry.Record(
            "auditing.insert_access_log",
            DatabaseProvider.MySql,
            DapperOperation.Execute,
            TimeSpan.FromMilliseconds(30),
            new OperationCanceledException());

        Assert.IsTrue(measurements.Any(measurement =>
            measurement.Name == "fullnet.data.sql.failures"
            && measurement.Value == 1d
            && measurement.Tags["statement_name"] == "auditing.insert_access_log"
            && measurement.Tags["provider"] == "my_sql"
            && measurement.Tags["operation"] == "execute"
            && measurement.Tags["outcome"] == "canceled"));
    }

    [TestMethod]
    public void Benchmark_aggregation_separates_controlled_cancellation_from_failure()
    {
        using var telemetry = new MixedLoadDapperTelemetry();

        DapperTelemetry.Record(
            "outbox.renew_lease",
            DatabaseProvider.MySql,
            DapperOperation.Execute,
            TimeSpan.FromMilliseconds(5),
            new OperationCanceledException());
        DapperTelemetry.Record(
            "outbox.mark_processed",
            DatabaseProvider.MySql,
            DapperOperation.Execute,
            TimeSpan.FromMilliseconds(5),
            new InvalidOperationException("database failure"));

        var snapshot = telemetry.Snapshot();

        Assert.AreEqual(1L, snapshot.Failures);
        Assert.AreEqual(1L, snapshot.Cancellations);
        Assert.AreEqual(
            1L,
            snapshot.FailureStatements["outbox.mark_processed"]);
        Assert.IsFalse(
            snapshot.FailureStatements.ContainsKey("outbox.renew_lease"));
        Assert.AreEqual(
            1L,
            snapshot.FailureReasons["application_error"]);
    }

    private static MeterListener CreateListener(List<Measurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == DapperTelemetry.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
                measurements.Add(new Measurement(
                    instrument.Name,
                    value,
                    CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
                measurements.Add(new Measurement(
                    instrument.Name,
                    value,
                    CopyTags(tags))));
        listener.Start();
        return listener;
    }

    private static IReadOnlyDictionary<string, string> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.ToArray().ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.ToString() ?? string.Empty,
            StringComparer.Ordinal);

    private sealed record Measurement(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string> Tags);
}
