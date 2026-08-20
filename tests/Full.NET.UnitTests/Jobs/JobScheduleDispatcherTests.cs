using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobScheduleDispatcherTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 0, 5, 20, TimeSpan.Zero);
    private static readonly Guid ScheduleId = Guid.CreateVersion7();
    private static readonly Guid DefinitionId = Guid.CreateVersion7();
    private static readonly Guid ExecutionId = Guid.CreateVersion7();

    [TestMethod]
    public async Task ProcessDueAsync_AtomicallyCreatesExecutionAndAdvancesSqlServerSchedule()
    {
        var store = new DispatchStore(
            DueSchedule(JobMisfirePolicies.FireOnce));
        var dispatcher = CreateDispatcher(
            store,
            DatabaseProvider.SqlServer);

        var created = await dispatcher.ProcessDueAsync(
            7,
            CancellationToken.None);

        Assert.AreEqual(1, created);
        Assert.AreSame(JobSql.SelectDueSchedulesSqlServer, store.QueryStatement);
        Assert.AreEqual(7, store.ObservedBatchSize);
        Assert.AreEqual(1, store.TransactionCount);
        CollectionAssert.AreEqual(
            new[]
            {
                JobSql.InsertScheduledExecution.Name,
                JobSql.AdvanceSchedule.Name,
            },
            store.CommandNames.ToArray());
        Assert.AreEqual(ExecutionId, store.ExecutionId);
        Assert.AreEqual(ScheduleId, store.ExecutionScheduleId);
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 31, 0, 5, 0, TimeSpan.Zero),
            store.ExecutionScheduledForUtc);
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 31, 0, 6, 0, TimeSpan.Zero),
            store.AdvancedNextExecutionAtUtc);
    }

    [TestMethod]
    public async Task ProcessDueAsync_SkipMisfireAdvancesWithoutExecution()
    {
        var schedule = DueSchedule(JobMisfirePolicies.Skip);
        schedule.NextExecutionAtUtc =
            new DateTimeOffset(2026, 7, 31, 0, 1, 0, TimeSpan.Zero);
        var store = new DispatchStore(schedule);
        var dispatcher = CreateDispatcher(
            store,
            DatabaseProvider.SqlServer);

        var created = await dispatcher.ProcessDueAsync(
            7,
            CancellationToken.None);

        Assert.AreEqual(0, created);
        CollectionAssert.AreEqual(
            new[] { JobSql.AdvanceSchedule.Name },
            store.CommandNames.ToArray());
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 31, 0, 6, 0, TimeSpan.Zero),
            store.AdvancedNextExecutionAtUtc);
    }

    [TestMethod]
    public async Task ProcessDueAsync_UsesMySqlSkipLockedSelection()
    {
        var store = new DispatchStore();
        var dispatcher = CreateDispatcher(
            store,
            DatabaseProvider.MySql);

        var created = await dispatcher.ProcessDueAsync(
            3,
            CancellationToken.None);

        Assert.AreEqual(0, created);
        Assert.AreSame(JobSql.SelectDueSchedulesMySql, store.QueryStatement);
        Assert.AreEqual(3, store.ObservedBatchSize);
    }

    [TestMethod]
    public async Task ProcessDueAsync_RollsBackWhenLockedScheduleDoesNotAdvance()
    {
        var store = new DispatchStore(
            DueSchedule(JobMisfirePolicies.FireOnce))
        {
            AdvanceAffectedRows = 0,
        };
        var dispatcher = CreateDispatcher(
            store,
            DatabaseProvider.SqlServer);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            dispatcher.ProcessDueAsync(1, CancellationToken.None));

        Assert.IsTrue(store.RollbackObserved);
    }

    private static JobScheduleDispatcher CreateDispatcher(
        DispatchStore store,
        DatabaseProvider provider) =>
        new(
            store,
            store,
            store,
            Options.Create(new DatabaseOptions { Provider = provider }),
            new FixedClock(Now),
            new FixedIdGenerator(ExecutionId));

    private static JobScheduleRecord DueSchedule(string misfirePolicy) =>
        new()
        {
            Id = ScheduleId,
            JobDefinitionId = DefinitionId,
            TriggerKind = JobTriggerKinds.Cron,
            CronExpression = "* * * * *",
            TimeZoneId = "UTC",
            MisfirePolicy = misfirePolicy,
            IsEnabled = true,
            NextExecutionAtUtc =
                new DateTimeOffset(2026, 7, 31, 0, 5, 0, TimeSpan.Zero),
            CreatedAtUtc = Now.AddDays(-1),
            CreatedByUserId = Guid.CreateVersion7(),
            Version = 4,
        };

    private sealed class DispatchStore(params JobScheduleRecord[] schedules)
        : IQueryExecutor, ICommandExecutor, ICommandTransaction
    {
        private bool _transactionActive;

        public SqlStatement? QueryStatement { get; private set; }

        public int? ObservedBatchSize { get; private set; }

        public int TransactionCount { get; private set; }

        public List<string> CommandNames { get; } = [];

        public Guid? ExecutionId { get; private set; }

        public Guid? ExecutionScheduleId { get; private set; }

        public DateTimeOffset? ExecutionScheduledForUtc { get; private set; }

        public DateTimeOffset? AdvancedNextExecutionAtUtc { get; private set; }

        public int AdvanceAffectedRows { get; init; } = 1;

        public bool RollbackObserved { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == JobSql.HasActiveRunningForDefinition)
            {
                return Task.FromResult<T?>(default);
            }

            throw new InvalidOperationException(
                $"Unexpected query '{statement.Name}'.");
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(_transactionActive);
            QueryStatement = statement;
            ObservedBatchSize = (int?)parameters?
                .GetType()
                .GetProperty("BatchSize")
                ?.GetValue(parameters);
            return Task.FromResult<IReadOnlyList<T>>(
                schedules.Cast<T>().ToArray());
        }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(_transactionActive);
            CommandNames.Add(statement.Name);
            var values = parameters?.GetType()
                .GetProperties()
                .ToDictionary(
                    property => property.Name,
                    property => property.GetValue(parameters),
                    StringComparer.Ordinal)
                ?? [];
            if (statement == JobSql.InsertScheduledExecution)
            {
                ExecutionId = (Guid?)values["Id"];
                ExecutionScheduleId = (Guid?)values["JobScheduleId"];
                ExecutionScheduledForUtc =
                    (DateTimeOffset?)values["ScheduledForUtc"];
                return Task.FromResult(1);
            }

            if (statement == JobSql.AdvanceSchedule)
            {
                AdvancedNextExecutionAtUtc =
                    (DateTimeOffset?)values["NextExecutionAtUtc"];
                return Task.FromResult(AdvanceAffectedRows);
            }

            throw new InvalidOperationException(
                $"Unexpected command '{statement.Name}'.");
        }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            TransactionCount++;
            _transactionActive = true;
            try
            {
                return await action(cancellationToken);
            }
            catch
            {
                RollbackObserved = true;
                throw;
            }
            finally
            {
                _transactionActive = false;
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
