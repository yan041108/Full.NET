using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobsBacklogReaderTests
{
    [TestMethod]
    public async Task ReadAsync_MapsSqlServerSnapshotAndStableParameters()
    {
        var observedAtUtc =
            new DateTimeOffset(2026, 7, 30, 1, 2, 3, TimeSpan.Zero);
        var oldestClaimableAtUtc = observedAtUtc.AddSeconds(-90);
        var oldestDueRetryAtUtc = observedAtUtc.AddSeconds(-120);
        var executor = new RecordingQueryExecutor(
            new JobsBacklogSqlServerRow
            {
                PendingCount = 2,
                OldestClaimableCreatedAtUtc = oldestClaimableAtUtc,
                DueRetryCount = 3,
                OldestDueRetryAtUtc = oldestDueRetryAtUtc,
            });
        var reader = CreateReader(executor, DatabaseProvider.SqlServer);

        var snapshot = await reader.ReadAsync(
            observedAtUtc,
            CancellationToken.None);

        Assert.AreSame(JobSql.ReadBacklogSqlServer, executor.Statement);
        Assert.AreEqual(observedAtUtc, Parameter<DateTimeOffset>(
            executor.Parameters,
            "ObservedAtUtc"));
        Assert.AreEqual(JobExecutionStatuses.Pending, Parameter<string>(
            executor.Parameters,
            "PendingStatus"));
        Assert.AreEqual(2, snapshot.PendingCount);
        Assert.AreEqual(
            oldestClaimableAtUtc,
            snapshot.OldestClaimableCreatedAtUtc);
        Assert.AreEqual(3, snapshot.DueRetryCount);
        Assert.AreEqual(oldestDueRetryAtUtc, snapshot.OldestDueRetryAtUtc);
    }

    [TestMethod]
    public async Task ReadAsync_MapsMySqlUtcValues()
    {
        var observedAtUtc =
            new DateTimeOffset(2026, 7, 30, 1, 2, 3, TimeSpan.Zero);
        var oldestClaimableAtUtc =
            observedAtUtc.AddSeconds(-90).UtcDateTime;
        var oldestDueRetryAtUtc =
            observedAtUtc.AddSeconds(-120).UtcDateTime;
        var executor = new RecordingQueryExecutor(
            new JobsBacklogMySqlRow
            {
                PendingCount = 2,
                OldestClaimableCreatedAtUtc = oldestClaimableAtUtc,
                DueRetryCount = 3,
                OldestDueRetryAtUtc = oldestDueRetryAtUtc,
            });
        var reader = CreateReader(executor, DatabaseProvider.MySql);

        var snapshot = await reader.ReadAsync(
            observedAtUtc,
            CancellationToken.None);

        Assert.AreSame(JobSql.ReadBacklogMySql, executor.Statement);
        Assert.AreEqual(
            new DateTimeOffset(oldestClaimableAtUtc, TimeSpan.Zero),
            snapshot.OldestClaimableCreatedAtUtc);
        Assert.AreEqual(
            new DateTimeOffset(oldestDueRetryAtUtc, TimeSpan.Zero),
            snapshot.OldestDueRetryAtUtc);
    }

    [TestMethod]
    public async Task ReadAsync_RejectsUnsupportedProvider()
    {
        var provider = (DatabaseProvider)999;
        var reader = CreateReader(
            new RecordingQueryExecutor(new object()),
            provider);

        var exception = await Assert.ThrowsExactlyAsync<
            InvalidOperationException>(
            () => reader.ReadAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, provider.ToString());
    }

    private static JobsBacklogReader CreateReader(
        IQueryExecutor queryExecutor,
        DatabaseProvider provider) =>
        new(
            queryExecutor,
            Options.Create(new DatabaseOptions { Provider = provider }));

    private static T Parameter<T>(object? parameters, string name) =>
        ReadSqlParameter<T>(parameters, name);

    private sealed class RecordingQueryExecutor(object row) : IQueryExecutor
    {
        public SqlStatement? Statement { get; private set; }

        public object? Parameters { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statement = statement;
            Parameters = parameters;
            return Task.FromResult((T?)row);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                $"Unexpected list statement '{statement.Name}'.");
    }
}
