using System.Data;
using System.Data.Common;
using System.Diagnostics.Metrics;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class DbSessionCapacityTests
{
    [TestMethod]
    public async Task AcquireConnectionAsync_ReleasesEachNonTransactionalLeaseImmediately()
    {
        var firstConnection = CreateOpenableConnection();
        var secondConnection = CreateOpenableConnection();
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.Create().Returns(firstConnection, secondConnection);
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry);
        await using var session = new DbSession(
            factory,
            gate,
            telemetry,
            new DatabaseAdmissionPriorityScope());

        await using (var firstLease = await session.AcquireConnectionAsync(
            CancellationToken.None))
        {
            Assert.AreSame(firstConnection, firstLease.Connection);
            Assert.IsNull(firstLease.Transaction);
            Assert.AreEqual(1, gate.InUseCount);
        }

        Assert.AreEqual(0, gate.InUseCount);
        await firstConnection.Received(1).DisposeAsync();

        await using (var secondLease = await session.AcquireConnectionAsync(
            CancellationToken.None))
        {
            Assert.AreSame(secondConnection, secondLease.Connection);
            Assert.AreEqual(1, gate.InUseCount);
        }

        Assert.AreEqual(0, gate.InUseCount);
        await secondConnection.Received(1).DisposeAsync();
        factory.Received(2).Create();
    }

    [TestMethod]
    public async Task AcquireConnectionAsync_BorrowsTransactionConnectionUntilCommit()
    {
        var transaction = Substitute.For<DbTransaction>();
        var connection = CreateOpenableConnection();
        connection
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<DbTransaction>(transaction));
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.Create().Returns(connection);
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry);
        await using var session = new DbSession(
            factory,
            gate,
            telemetry,
            new DatabaseAdmissionPriorityScope());
        await session.BeginAsync(CancellationToken.None);

        await using (var firstLease = await session.AcquireConnectionAsync(
            CancellationToken.None))
        await using (var secondLease = await session.AcquireConnectionAsync(
            CancellationToken.None))
        {
            Assert.AreSame(connection, firstLease.Connection);
            Assert.AreSame(connection, secondLease.Connection);
            Assert.AreSame(transaction, firstLease.Transaction);
            Assert.AreSame(transaction, secondLease.Transaction);
        }

        Assert.AreEqual(1, gate.InUseCount);
        await connection.DidNotReceive().DisposeAsync();

        await session.CommitAsync(CancellationToken.None);

        Assert.AreEqual(0, gate.InUseCount);
        await transaction.Received(1).CommitAsync(CancellationToken.None);
        await connection.Received(1).DisposeAsync();
        factory.Received(1).Create();
    }

    [TestMethod]
    public async Task AcquireConnectionAsync_ReleasesPermitWhenOpenFails()
    {
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);
        connection
            .OpenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("open failed")));
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.Create().Returns(connection);
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry);
        await using var session = new DbSession(
            factory,
            gate,
            telemetry,
            new DatabaseAdmissionPriorityScope());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.AcquireConnectionAsync(CancellationToken.None));

        Assert.AreEqual(0, gate.InUseCount);
    }

    [TestMethod]
    public async Task ConnectionLease_ReleasesPermitOnlyAfterConnectionDisposal()
    {
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);
        connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.Create().Returns(connection);
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry);
        await using var session = new DbSession(
            factory,
            gate,
            telemetry,
            new DatabaseAdmissionPriorityScope());
        var lease = await session.AcquireConnectionAsync(CancellationToken.None);
        Assert.AreEqual(1, gate.InUseCount);

        await lease.DisposeAsync();

        Assert.AreEqual(0, gate.InUseCount);
        await connection.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task DisposeAsync_ReleasesConnectionAndPermitWhenTransactionDisposalFails()
    {
        var transaction = Substitute.For<DbTransaction>();
        transaction
            .DisposeAsync()
            .Returns(ValueTask.FromException(new InvalidOperationException("dispose failed")));
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);
        connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        connection
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<DbTransaction>(transaction));
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.Create().Returns(connection);
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry);
        var session = new DbSession(
            factory,
            gate,
            telemetry,
            new DatabaseAdmissionPriorityScope());
        await session.BeginAsync(CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.DisposeAsync().AsTask());

        Assert.AreEqual(0, gate.InUseCount);
        await connection.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task AcquireConnectionAsync_UsesCriticalReserveInsidePriorityScope()
    {
        var firstConnection = CreateOpenableConnection();
        var secondConnection = CreateOpenableConnection();
        var firstFactory = Substitute.For<IDbConnectionFactory>();
        firstFactory.Create().Returns(firstConnection);
        var secondFactory = Substitute.For<IDbConnectionFactory>();
        secondFactory.Create().Returns(secondConnection);
        using var telemetry = CreateTelemetry();
        using var gate = new DatabaseAdmissionGate(
            Options.Create(new DatabaseCapacityOptions
            {
                Enabled = true,
                HostRole = DatabaseHostRole.Worker,
                PermitLimit = 1,
                CriticalWorkerReserve = 1,
                QueueLimit = 0,
                AcquireTimeoutMilliseconds = 100,
            }),
            telemetry);
        var normalPriority = new DatabaseAdmissionPriorityScope();
        var criticalPriority = new DatabaseAdmissionPriorityScope();
        await using var normalSession = new DbSession(
            firstFactory,
            gate,
            telemetry,
            normalPriority);
        await using var criticalSession = new DbSession(
            secondFactory,
            gate,
            telemetry,
            criticalPriority);
        await using var normalLease = await normalSession.AcquireConnectionAsync(
            CancellationToken.None);

        using (criticalPriority.EnterCritical())
        {
            await using var criticalLease = await criticalSession.AcquireConnectionAsync(
                CancellationToken.None);
            Assert.AreEqual(2, gate.InUseCount);
        }

        Assert.AreEqual(1, gate.InUseCount);
    }

    [TestMethod]
    public async Task AcquireConnectionAsync_WaitMetricIncludesProviderOpenTime()
    {
        var waits = new List<double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == DatabaseConnectionTelemetry.MeterName
                    && instrument.Name == "fullnet.db.connection.wait")
                {
                    current.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
            {
                var provider = tags.ToArray().SingleOrDefault(tag =>
                    tag.Key == "provider").Value?.ToString();
                var outcome = tags.ToArray().SingleOrDefault(tag =>
                    tag.Key == "outcome").Value?.ToString();
                if (provider == "sqlserver" && outcome == "success")
                {
                    waits.Add(value);
                }
            });
        listener.Start();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);
        connection
            .OpenAsync(Arg.Any<CancellationToken>())
            .Returns(async _ => await Task.Delay(80));
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.Create().Returns(connection);
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry);
        await using var session = new DbSession(
            factory,
            gate,
            telemetry,
            new DatabaseAdmissionPriorityScope());

        await using var lease = await session.AcquireConnectionAsync(
            CancellationToken.None);

        Assert.IsTrue(waits.Any(wait => wait >= 0.05d));
    }

    private static DatabaseAdmissionGate CreateGate(
        DatabaseConnectionTelemetry telemetry) => new(
        Options.Create(new DatabaseCapacityOptions
        {
            Enabled = true,
            HostRole = DatabaseHostRole.Api,
            PermitLimit = 1,
            QueueLimit = 0,
            AcquireTimeoutMilliseconds = 100,
        }),
        telemetry);

    private static DatabaseConnectionTelemetry CreateTelemetry() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
        }),
        Options.Create(new DatabaseCapacityOptions
        {
            HostRole = DatabaseHostRole.Api,
        }));

    private static DbConnection CreateOpenableConnection()
    {
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);
        connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return connection;
    }
}
