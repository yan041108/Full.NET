using System.Data.Common;
using Full.NET.Benchmarks.Outbox;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Performance;

/// <summary>
/// 验证 Outbox 写入 Profile 只在显式请求时加入 Typed Plan 候选路径。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class OutboxWriteProfileContractTests
{
    [TestMethod]
    public void Defaults_to_registry_path_only()
    {
        var options = OutboxWriteProfileOptions.Parse([]);

        CollectionAssert.AreEqual(
            new[] { OutboxWriteProfileCommandPath.Registry },
            options.CommandPaths.ToArray());
    }

    [TestMethod]
    public void Parses_ordered_registry_and_typed_paths()
    {
        var options = OutboxWriteProfileOptions.Parse(
            ["--command-paths", "registry,typed"]);

        CollectionAssert.AreEqual(
            new[]
            {
                OutboxWriteProfileCommandPath.Registry,
                OutboxWriteProfileCommandPath.Typed,
            },
            options.CommandPaths.ToArray());
    }

    [TestMethod]
    public void Rejects_unknown_or_duplicate_command_paths()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            OutboxWriteProfileOptions.Parse(
                ["--command-paths", "registry,dynamic"]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            OutboxWriteProfileOptions.Parse(
                ["--command-paths", "typed,typed"]));
    }

    [TestMethod]
    public void Scenario_matrix_reverses_path_order_on_even_repetitions()
    {
        var options = OutboxWriteProfileOptions.Parse(
            [
                "--targets", "legacy",
                "--concurrency", "1",
                "--repetitions", "2",
                "--command-paths", "registry,typed",
            ]);

        var order = OutboxWriteProfileScenarioMatrix
            .Create(options)
            .Select(scenario =>
                $"{scenario.Repetition}:{scenario.CommandPath}")
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "1:Registry",
                "1:Typed",
                "2:Typed",
                "2:Registry",
            },
            order);
    }

    [TestMethod]
    public void Command_paths_expose_stable_lowercase_tokens()
    {
        Assert.AreEqual(
            "registry",
            OutboxWriteProfileCommandPath.Registry.ToToken());
        Assert.AreEqual(
            "typed",
            OutboxWriteProfileCommandPath.Typed.ToToken());
    }

    [TestMethod]
    public void Connection_acquisition_capture_uses_fullnet_boundary_for_sqlserver()
    {
        using var capture = new MixedLoadDatabaseConnectionTelemetry("sqlserver");
        using var telemetry = new DatabaseConnectionTelemetry(
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }),
            Options.Create(new DatabaseCapacityOptions()));

        telemetry.RecordAcquisition(
            DatabaseConnectionAcquireOutcome.Success,
            TimeSpan.FromMilliseconds(12));
        telemetry.RecordAcquisition(
            DatabaseConnectionAcquireOutcome.Failure,
            TimeSpan.FromMilliseconds(35));

        var snapshot = capture.Snapshot();

        Assert.AreEqual(2, snapshot.Attempts);
        Assert.AreEqual(1, snapshot.Outcomes["success"]);
        Assert.AreEqual(1, snapshot.Outcomes["failure"]);
        Assert.IsNotNull(snapshot.WaitDuration);
        Assert.AreEqual(12d, snapshot.WaitDuration.MinimumMilliseconds, 0.001d);
        Assert.AreEqual(35d, snapshot.WaitDuration.MaximumMilliseconds, 0.001d);
    }

    [TestMethod]
    public void Failure_classifier_unwraps_stable_database_code_and_window_owner()
    {
        var exception = new DataCommandException(
            DataCommandFailureKind.UniqueConstraint,
            new StubDbException(2601));

        var failure = OutboxWriteProfileFailureClassifier.Classify(
            exception,
            windowCancellationRequested: true);

        Assert.AreEqual("unique_constraint", failure.Reason);
        Assert.AreEqual("2601", failure.DatabaseErrorCode);
        Assert.IsTrue(failure.WindowOwned);
    }

    [TestMethod]
    public void Connection_acquisition_capture_is_bounded_and_reports_overflow()
    {
        using var capture = new MixedLoadDatabaseConnectionTelemetry(
            "sqlserver",
            sampleCapacity: 2);
        using var telemetry = new DatabaseConnectionTelemetry(
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }),
            Options.Create(new DatabaseCapacityOptions()));

        telemetry.RecordAcquisition(
            DatabaseConnectionAcquireOutcome.Success,
            TimeSpan.FromMilliseconds(1));
        telemetry.RecordAcquisition(
            DatabaseConnectionAcquireOutcome.Success,
            TimeSpan.FromMilliseconds(2));
        telemetry.RecordAcquisition(
            DatabaseConnectionAcquireOutcome.Success,
            TimeSpan.FromMilliseconds(3));

        var snapshot = capture.Snapshot();

        Assert.AreEqual(3, snapshot.Attempts);
        Assert.AreEqual(2, snapshot.CapturedSamples);
        Assert.AreEqual(1, snapshot.DroppedSamples);
        Assert.IsFalse(snapshot.EvidenceComplete);
    }

    private sealed class StubDbException(int errorCode) : DbException
    {
        public override int ErrorCode => errorCode;
    }
}
