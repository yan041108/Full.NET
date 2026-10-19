using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

/// <summary>阶段观测必须贴合真实租约生命周期，不能把拒绝计为打开或重复计量事务借用。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DatabaseConnectionPhaseTests
{
    [TestMethod]
    [DataRow("single")]
    [DataRow("query")]
    [DataRow("execute")]
    [DataRow("multiple")]
    public async Task Failed_connection_open_does_not_create_a_sql_command_sample(string operation)
    {
        using var capture = new PhaseCapture();
        using var fixture = new Fixture();
        fixture.Connection.OpenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("open failed")));
        await using var session = fixture.Session();
        var executor = new DapperSqlExecutor(session, Substitute.For<ICurrentTenant>(),
            Options.Create(new DatabaseOptions()), NullLogger<DapperSqlExecutor>.Instance);
        var statement = new SqlStatement("test.phase_boundary", "SELECT 1", SqlDataScope.Global);
        Task Execute() => operation switch
        {
            "single" => executor.QuerySingleOrDefaultAsync<int>(statement),
            "query" => executor.QueryAsync<int>(statement),
            "execute" => executor.ExecuteAsync(statement),
            _ => executor.QueryMultipleAsync(statement, null, (_, _) => Task.FromResult(1)),
        };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(Execute);
        Assert.AreEqual(0, capture.CommandSamples.Count);
        CollectionAssert.AreEqual(new[] { "admission", "open" }, capture.Rows.Select(row => row.Phase).ToArray());
    }

    [TestMethod]
    [DataRow("normal")]
    [DataRow("critical")]
    [DataRow("authorization")]
    public async Task Successful_lease_records_each_phase_once_with_bounded_access_mode(string mode)
    {
        using var capture = new PhaseCapture();
        using var fixture = new Fixture();
        // 同时进入关键范围时，授权读取仍按实际选择路径标记，不能误报为普通许可。
        using var critical = mode != "normal" ? fixture.Priority.EnterCritical() : null;
        using var authorization = mode == "authorization" ? fixture.Authorization.Enter() : null;
        await using var session = fixture.Session();
        var lease = await session.AcquireConnectionAsync(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "admission", "open" }, capture.Rows.Select(row => row.Phase).ToArray());
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        CollectionAssert.AreEqual(new[] { "admission", "open", "hold" }, capture.Rows.Select(row => row.Phase).ToArray());
        Assert.AreEqual(0, fixture.Gate.InUseCount);
        foreach (var row in capture.Rows)
        {
            Assert.AreEqual(mode, row.Tags["access_mode"]);
            Assert.AreEqual("s", row.Unit);
            Assert.IsTrue(double.IsFinite(row.Seconds) && row.Seconds >= 0);
            CollectionAssert.AreEquivalent(new[] { "provider", "host_role", "phase", "access_mode" }, row.Tags.Keys.ToArray());
        }
    }

    [TestMethod]
    public async Task Rejected_admission_records_no_open_or_hold_sample()
    {
        using var capture = new PhaseCapture();
        using var fixture = new Fixture();
        await using var occupied = await fixture.Gate.AcquireAsync(CancellationToken.None);
        await using var session = fixture.Session();
        await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(() => session.AcquireConnectionAsync(CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "admission" }, capture.Rows.Select(row => row.Phase).ToArray());
        await fixture.Connection.DidNotReceive().OpenAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_or_canceled_open_records_attempt_without_hold_and_releases_permit(bool cancel)
    {
        using var capture = new PhaseCapture();
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (cancel) { cancellation.Cancel(); return Task.FromCanceled(cancellation.Token); }
            return Task.FromException(new InvalidOperationException("provider open failed"));
        });
        await using var session = fixture.Session();
        if (cancel)
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => session.AcquireConnectionAsync(cancellation.Token));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.AcquireConnectionAsync(cancellation.Token));
        CollectionAssert.AreEqual(new[] { "admission", "open" }, capture.Rows.Select(row => row.Phase).ToArray());
        Assert.AreEqual(0, fixture.Gate.InUseCount);
        await fixture.Connection.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task Borrowed_transaction_connections_do_not_repeat_acquisition_or_hold_measurements()
    {
        using var capture = new PhaseCapture();
        using var fixture = new Fixture();
        var transaction = Substitute.For<DbTransaction>();
        fixture.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<DbTransaction>(transaction));
        await using var session = fixture.Session();
        await session.BeginAsync(CancellationToken.None);
        for (var index = 0; index < 3; index++)
        {
            await using var borrowed = await session.AcquireConnectionAsync(CancellationToken.None);
            Assert.AreSame(transaction, borrowed.Transaction);
        }
        Assert.AreEqual(2, capture.Rows.Count);
        await session.DisposeAsync();
        CollectionAssert.AreEqual(new[] { "admission", "open", "hold" }, capture.Rows.Select(row => row.Phase).ToArray());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DatabaseConnectionTelemetry _telemetry;
        private readonly IDbConnectionFactory _factory = Substitute.For<IDbConnectionFactory>();
        public DbConnection Connection { get; } = Substitute.For<DbConnection>();
        public DatabaseAdmissionPriorityScope Priority { get; } = new();
        public DatabaseAuthorizationReadScope Authorization { get; }
        public DatabaseAdmissionGate Gate { get; }

        public Fixture()
        {
            var database = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
            var capacity = Options.Create(new DatabaseCapacityOptions
            {
                Enabled = true, HostRole = DatabaseHostRole.Worker, PermitLimit = 1,
                CriticalWorkerReserve = 1, AuthorizationReadReserve = 1, QueueLimit = 0,
            });
            _telemetry = new DatabaseConnectionTelemetry(database, capacity);
            Gate = new DatabaseAdmissionGate(capacity, _telemetry);
            Authorization = new DatabaseAuthorizationReadScope(capacity, database);
            Connection.State.Returns(ConnectionState.Closed);
            Connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            _factory.Create().Returns(Connection);
        }

        public DbSession Session() => new(_factory, Gate, _telemetry, Priority, Authorization);
        public void Dispose() { Gate.Dispose(); _telemetry.Dispose(); }
    }

    private sealed class PhaseCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public ConcurrentQueue<PhaseMeasurement> Rows { get; } = new();
        public ConcurrentQueue<double> CommandSamples { get; } = new();
        public PhaseCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if ((instrument.Meter.Name == DatabaseConnectionTelemetry.MeterName
                    && instrument.Name == "fullnet.db.connection.phase.duration")
                    || (instrument.Meter.Name == DapperTelemetry.MeterName
                    && instrument.Name == "fullnet.data.sql.command.duration"))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                if (instrument.Name == "fullnet.data.sql.command.duration") { CommandSamples.Enqueue(value); return; }
                var copied = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? "");
                Rows.Enqueue(new PhaseMeasurement(copied["phase"], instrument.Unit, value, copied));
            });
            _listener.Start();
        }
        public void Dispose() => _listener.Dispose();
    }

    private sealed record PhaseMeasurement(string Phase, string? Unit, double Seconds, Dictionary<string, string> Tags);
}
