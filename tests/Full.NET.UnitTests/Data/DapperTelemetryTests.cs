using System.Diagnostics.Metrics;
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
