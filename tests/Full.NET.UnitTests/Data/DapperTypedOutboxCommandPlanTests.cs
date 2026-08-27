using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using Microsoft.Data.SqlClient;

namespace Full.NET.UnitTests.Data;

/// <summary>
/// 验证 Outbox 强类型固定 Plan 只复用空闲命令，并按 Provider 隔离回收槽。
/// </summary>
[TestClass]
public sealed class DapperTypedOutboxCommandPlanTests
{
    [TestMethod]
    public void Legacy_plan_reuses_command_and_updates_parameters_by_ordinal()
    {
        var plan = new OutboxInsertTypedCommandPlan();
        using var firstConnection = new SqlConnection();
        var firstMessage = CreateLegacyMessage(1, "first");
        var first = plan.GetCommand(
            firstConnection,
            DatabaseProvider.SqlServer,
            firstMessage);
        var firstIdParameter = first.Parameters[0];

        Assert.AreEqual(firstMessage.Id, first.Parameters[0].Value);
        Assert.AreEqual(firstMessage.MessageType, first.Parameters[1].Value);
        Assert.IsTrue(plan.TryRecycle(DatabaseProvider.SqlServer, first));
        Assert.IsNull(first.Connection);
        Assert.IsNull(first.Transaction);
        Assert.AreEqual(DBNull.Value, first.Parameters[0].Value);

        using var secondConnection = new SqlConnection();
        var secondMessage = CreateLegacyMessage(2, "second");
        var second = plan.GetCommand(
            secondConnection,
            DatabaseProvider.SqlServer,
            secondMessage);

        Assert.AreSame(first, second);
        Assert.AreSame(firstIdParameter, second.Parameters[0]);
        Assert.AreSame(secondConnection, second.Connection);
        Assert.AreEqual(secondMessage.Id, second.Parameters[0].Value);
        Assert.AreEqual("second", second.Parameters[5].Value);
        second.Dispose();
    }

    [TestMethod]
    public void Legacy_plan_never_shares_in_use_command_and_caches_one_idle_command()
    {
        var plan = new OutboxInsertTypedCommandPlan();
        using var firstConnection = new SqlConnection();
        using var secondConnection = new SqlConnection();
        var first = plan.GetCommand(
            firstConnection,
            DatabaseProvider.SqlServer,
            CreateLegacyMessage(3, "first"));
        var second = plan.GetCommand(
            secondConnection,
            DatabaseProvider.SqlServer,
            CreateLegacyMessage(4, "second"));

        Assert.AreNotSame(first, second);
        Assert.IsTrue(plan.TryRecycle(DatabaseProvider.SqlServer, first));
        Assert.IsFalse(plan.TryRecycle(DatabaseProvider.SqlServer, second));
        second.Dispose();
    }

    [TestMethod]
    public void Legacy_plan_isolates_provider_slots()
    {
        var plan = new OutboxInsertTypedCommandPlan();
        using var sqlServerConnection = new SqlConnection();
        using var mySqlSlotConnection = new SqlConnection();
        var sqlServer = plan.GetCommand(
            sqlServerConnection,
            DatabaseProvider.SqlServer,
            CreateLegacyMessage(5, "sqlserver"));
        var mySql = plan.GetCommand(
            mySqlSlotConnection,
            DatabaseProvider.MySql,
            CreateLegacyMessage(6, "mysql"));

        Assert.AreNotSame(sqlServer, mySql);
        Assert.IsTrue(plan.TryRecycle(DatabaseProvider.SqlServer, sqlServer));
        Assert.IsTrue(plan.TryRecycle(DatabaseProvider.MySql, mySql));
    }

    [TestMethod]
    public void Append_plan_binds_complete_fixed_shape_by_ordinal()
    {
        var plan = new AppendOnlyOutboxInsertTypedCommandPlan();
        using var connection = new SqlConnection();
        var message = CreateAppendMessage();
        var command = plan.GetCommand(
            connection,
            DatabaseProvider.SqlServer,
            message);

        Assert.AreEqual(12, command.Parameters.Count);
        Assert.AreEqual(message.Id, command.Parameters[0].Value);
        Assert.AreEqual(message.PartitionKey, command.Parameters[5].Value);
        Assert.AreEqual(message.Producer, command.Parameters[9].Value);
        Assert.AreSame(message.Payload, command.Parameters[10].Value);
        Assert.AreEqual(message.OccurredAtUtc.UtcDateTime, command.Parameters[11].Value);
        command.Dispose();
    }

    [TestMethod]
    public void Legacy_plan_preserves_guid_and_utc_parameter_invariants()
    {
        var plan = new OutboxInsertTypedCommandPlan();
        using var connection = new SqlConnection();
        var localTime = new DateTimeOffset(
            2026,
            8,
            28,
            8,
            30,
            0,
            TimeSpan.FromHours(8));
        var message = CreateLegacyMessage(7, "typed") with
        {
            TenantId = null,
            OccurredAtUtc = localTime,
        };

        var command = plan.GetCommand(
            connection,
            DatabaseProvider.SqlServer,
            message);

        Assert.AreEqual(DbType.Guid, command.Parameters[0].DbType);
        Assert.AreEqual(DbType.Guid, command.Parameters[4].DbType);
        Assert.AreEqual(DBNull.Value, command.Parameters[4].Value);
        Assert.AreEqual(localTime.UtcDateTime, command.Parameters[7].Value);
        Assert.IsInstanceOfType<DateTime>(command.Parameters[7].Value);
        Assert.AreEqual(
            DateTimeKind.Utc,
            ((DateTime)command.Parameters[7].Value!).Kind);
        command.Dispose();
    }

    [TestMethod]
    public void Legacy_plan_rejects_unassigned_id()
    {
        var plan = new OutboxInsertTypedCommandPlan();
        using var connection = new SqlConnection();
        var message = CreateLegacyMessage(8, "empty") with { Id = Guid.Empty };

        Assert.ThrowsExactly<ArgumentException>(() =>
            plan.GetCommand(
                connection,
                DatabaseProvider.SqlServer,
                message));
    }

    [TestMethod]
    public void Wrapped_connection_attaches_inner_connection_to_typed_command()
    {
        var plan = new OutboxInsertTypedCommandPlan();
        var innerConnection = new SqlConnection();
        using var connection = new TestWrappedConnection(innerConnection);

        var command = plan.GetCommand(
            connection,
            DatabaseProvider.MySql,
            CreateLegacyMessage(9, "wrapped"));

        Assert.AreSame(innerConnection, command.Connection);
        command.Dispose();
    }

    private sealed class TestWrappedConnection(DbConnection inner)
        : DbConnection, IDapperDbConnectionWrapper
    {
        public DbConnection InnerConnection { get; } = inner;

        [AllowNull]
        public override string ConnectionString
        {
            get => InnerConnection.ConnectionString;
            set => InnerConnection.ConnectionString = value;
        }

        public override string Database => InnerConnection.Database;

        public override string DataSource => InnerConnection.DataSource;

        public override string ServerVersion => InnerConnection.ServerVersion;

        public override ConnectionState State => InnerConnection.State;

        public override void ChangeDatabase(string databaseName) =>
            InnerConnection.ChangeDatabase(databaseName);

        public override void Close() => InnerConnection.Close();

        public override void Open() => InnerConnection.Open();

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel) =>
            InnerConnection.BeginTransaction(isolationLevel);

        protected override DbCommand CreateDbCommand() =>
            InnerConnection.CreateCommand();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                InnerConnection.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static OutboxMessage CreateLegacyMessage(int suffix, string traceId) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"),
            "fullnet.test.entity.changed",
            1,
            "application/x-memorypack",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            traceId,
            [1, 2, 3],
            new DateTimeOffset(2026, 8, 28, 0, 0, suffix, TimeSpan.Zero));

    private static AppendOnlyOutboxMessage CreateAppendMessage() =>
        new(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "fullnet.test.entity.changed",
            2,
            "application/x-memorypack",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "partition-1",
            "correlation-1",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            "fullnet.test",
            [4, 5, 6],
            new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero));
}
