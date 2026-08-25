using System.Diagnostics.Metrics;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class DatabaseConnectionTelemetryTests
{
    [TestMethod]
    public void Record_EmitsWaitHoldAndOutcomeWithOnlyBoundedTags()
    {
        var measurements = new List<Measurement>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == DatabaseConnectionTelemetry.MeterName)
                {
                    current.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => measurements.Add(
                new Measurement(instrument.Name, value, CopyTags(tags))));
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => measurements.Add(
                new Measurement(instrument.Name, value, CopyTags(tags))));
        listener.Start();
        using var telemetry = new DatabaseConnectionTelemetry(
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
            }),
            Options.Create(new DatabaseCapacityOptions
            {
                HostRole = DatabaseHostRole.Worker,
            }));

        telemetry.RecordAcquisition(
            DatabaseConnectionAcquireOutcome.Success,
            TimeSpan.FromMilliseconds(125));
        telemetry.RecordHold(TimeSpan.FromSeconds(2));

        var wait = measurements.Single(measurement =>
            measurement.Name == "fullnet.db.connection.wait"
            && measurement.Tags.GetValueOrDefault("provider") == "mysql");
        Assert.AreEqual(0.125d, wait.Value, 0.0001d);
        CollectionAssert.AreEquivalent(
            new[] { "provider", "host_role", "outcome" },
            wait.Tags.Keys.ToArray());
        Assert.AreEqual("mysql", wait.Tags["provider"]);
        Assert.AreEqual("worker", wait.Tags["host_role"]);
        Assert.AreEqual("success", wait.Tags["outcome"]);

        var hold = measurements.Single(measurement =>
            measurement.Name == "fullnet.db.connection.hold"
            && measurement.Tags.GetValueOrDefault("provider") == "mysql");
        Assert.AreEqual(2d, hold.Value, 0.0001d);
        CollectionAssert.AreEquivalent(
            new[] { "provider", "host_role" },
            hold.Tags.Keys.ToArray());
    }

    private static Dictionary<string, string> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        }

        return copy;
    }

    private sealed record Measurement(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string> Tags);
}
