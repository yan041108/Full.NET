using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Retention;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditDetailsCleanupCheckpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task SqlServer_checkpoint_records_oldest_after_successful_cleanup_in_transaction()
    {
        var oldest = Now.AddMinutes(-2);
        var query = new RecordingQueryExecutor(oldest);
        var command = new RecordingCommandExecutor();
        var transaction = new RecordingTransaction();
        var store = CreateStore(DatabaseProvider.SqlServer, query, command, transaction);

        var snapshot = await store.RecordSuccessfulPassAsync(CancellationToken.None);

        Assert.AreEqual(Now, snapshot.LastSuccessfulCleanupAtUtc);
        Assert.AreEqual(oldest, snapshot.OldestExpiredAtUtc);
        Assert.AreEqual("auditing.details.checkpoint.oldest.sql_server", query.Statement?.Name);
        Assert.AreEqual("auditing.details.checkpoint.upsert.sql_server", command.Statement?.Name);
        Assert.AreEqual(1, transaction.Executions);
        Assert.IsTrue(command.Statement!.Text.Contains("HOLDLOCK", StringComparison.Ordinal));
        Assert.IsTrue(command.Statement.Text.Contains("LastSuccessfulCleanupAtUtc < @ObservedAtUtc", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MySql_checkpoint_converts_timestamp_to_utc_and_uses_single_upsert()
    {
        var oldest = Now.AddMinutes(-3);
        var query = new RecordingQueryExecutor(DateTime.SpecifyKind(oldest.DateTime, DateTimeKind.Unspecified));
        var command = new RecordingCommandExecutor();
        var transaction = new RecordingTransaction();
        var store = CreateStore(DatabaseProvider.MySql, query, command, transaction);

        var snapshot = await store.RecordSuccessfulPassAsync(CancellationToken.None);

        Assert.AreEqual(oldest, snapshot.OldestExpiredAtUtc);
        Assert.AreEqual("auditing.details.checkpoint.oldest.my_sql", query.Statement?.Name);
        Assert.AreEqual("auditing.details.checkpoint.upsert.my_sql", command.Statement?.Name);
        Assert.AreEqual(0, transaction.Executions);
        Assert.IsTrue(command.Statement!.Text.Contains("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Failed_backlog_read_does_not_refresh_checkpoint()
    {
        var command = new RecordingCommandExecutor();
        var store = CreateStore(DatabaseProvider.SqlServer,
            new RecordingQueryExecutor(null, fail: true), command, new RecordingTransaction());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.RecordSuccessfulPassAsync(CancellationToken.None));

        Assert.IsNull(command.Statement);
    }

    [TestMethod]
    public async Task MySql_checkpoint_read_converts_utc_columns_without_updating_state()
    {
        var query = new RecordingQueryExecutor(new AuditDetailsCleanupCheckpointMySqlRow
        {
            LastSuccessfulCleanupAtUtc = Now.DateTime,
            OldestExpiredAtUtc = Now.AddMinutes(-2).DateTime,
        });
        var command = new RecordingCommandExecutor();
        var store = CreateStore(DatabaseProvider.MySql, query, command, new RecordingTransaction());

        var snapshot = await store.ReadAsync(CancellationToken.None);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(Now, snapshot.Value.LastSuccessfulCleanupAtUtc);
        Assert.AreEqual(Now.AddMinutes(-2), snapshot.Value.OldestExpiredAtUtc);
        Assert.AreEqual("auditing.details.checkpoint.read.my_sql", query.Statement?.Name);
        Assert.IsNull(command.Statement);
    }

    [TestMethod]
    public async Task Missing_checkpoint_remains_unknown()
    {
        var query = new RecordingQueryExecutor(null);
        var store = CreateStore(DatabaseProvider.SqlServer, query,
            new RecordingCommandExecutor(), new RecordingTransaction());

        Assert.IsNull(await store.ReadAsync(CancellationToken.None));
        Assert.AreEqual("auditing.details.checkpoint.read.sql_server", query.Statement?.Name);
    }

    private static AuditDetailsCleanupCheckpointStore CreateStore(
        DatabaseProvider provider,
        IQueryExecutor query,
        ICommandExecutor command,
        ICommandTransaction transaction) =>
        new(query, command, transaction, new FixedClock(), new GuidV7IdGenerator(),
            Options.Create(new DatabaseOptions { Provider = provider }));

    private sealed class RecordingQueryExecutor(object? result, bool fail = false) : IQueryExecutor
    {
        public SqlStatement? Statement { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statement = statement;
            if (fail)
            {
                throw new InvalidOperationException("read failed");
            }

            return Task.FromResult((T?)result);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected list query.");
    }

    private sealed class RecordingCommandExecutor : ICommandExecutor
    {
        public SqlStatement? Statement { get; private set; }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statement = statement;
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public int Executions { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            Executions++;
            return await action(cancellationToken);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
