using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Features.ManageHostJobSchedules;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class HostJobScheduleServiceTests
{
    private static readonly Guid DefinitionId = Guid.CreateVersion7();
    private static readonly Guid ScheduleId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CreateAsync_RejectsDisabledDefinition()
    {
        var store = new ScheduleStore(isDefinitionEnabled: false);
        var service = CreateService(store);

        var result = await service.CreateAsync(
            ActorId,
            new CreateHostJobScheduleRequest(
                DefinitionId,
                JobTriggerKinds.Cron,
                "* * * * *",
                "UTC",
                null,
                JobMisfirePolicies.Skip));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(JobsErrorCodes.DefinitionDisabled, result.Error?.Code);
        Assert.AreEqual(0, store.CommandCount);
    }

    [TestMethod]
    public async Task CreateAsync_NormalizesWindowsTimeZoneAndCalculatesCronNextInstant()
    {
        var store = new ScheduleStore();
        var service = CreateService(store);

        var result = await service.CreateAsync(
            ActorId,
            new CreateHostJobScheduleRequest(
                DefinitionId,
                JobTriggerKinds.Cron,
                "0 9 * * *",
                "Eastern Standard Time",
                null,
                JobMisfirePolicies.FireOnce));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(ScheduleId, result.Value.Id);
        Assert.AreEqual("America/New_York", result.Value.TimeZoneId);
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 31, 13, 0, 0, TimeSpan.Zero),
            result.Value.NextExecutionAtUtc);
        Assert.IsTrue(result.Value.IsEnabled);
        Assert.AreEqual(1, result.Value.Version);
    }

    [TestMethod]
    public async Task CreateAsync_RejectsOneTimeSkipCombination()
    {
        var store = new ScheduleStore();
        var service = CreateService(store);

        var result = await service.CreateAsync(
            ActorId,
            new CreateHostJobScheduleRequest(
                DefinitionId,
                JobTriggerKinds.OneTime,
                null,
                "UTC",
                Now.AddHours(1),
                JobMisfirePolicies.Skip));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            JobsErrorCodes.ScheduleValidationFailed,
            result.Error?.Code);
        Assert.AreEqual(0, store.CommandCount);
    }

    [TestMethod]
    public async Task PauseAsync_RetainsNextInstantAndAdvancesVersion()
    {
        var next = Now.AddMinutes(5);
        var store = new ScheduleStore
        {
            Schedule = ExistingSchedule(
                isEnabled: true,
                nextExecutionAtUtc: next),
        };
        var service = CreateService(store);

        var result = await service.PauseAsync(
            ActorId,
            ScheduleId,
            new ChangeHostJobScheduleStateRequest(3));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.IsFalse(result.Value.IsEnabled);
        Assert.AreEqual(next, result.Value.NextExecutionAtUtc);
        Assert.AreEqual(4, result.Value.Version);
    }

    [TestMethod]
    public async Task ResumeAsync_RecalculatesCronFromCurrentInstant()
    {
        var store = new ScheduleStore
        {
            Schedule = ExistingSchedule(
                isEnabled: false,
                nextExecutionAtUtc: Now.AddDays(-1)),
        };
        var service = CreateService(store);

        var result = await service.ResumeAsync(
            ActorId,
            ScheduleId,
            new ChangeHostJobScheduleStateRequest(3));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.IsTrue(result.Value.IsEnabled);
        Assert.AreEqual(Now.AddMinutes(1), result.Value.NextExecutionAtUtc);
        Assert.AreEqual(4, result.Value.Version);
    }

    [TestMethod]
    public async Task ListAsync_UsesProviderSpecificStablePageQuery()
    {
        var store = new ScheduleStore
        {
            Schedule = ExistingSchedule(
                isEnabled: true,
                nextExecutionAtUtc: Now.AddMinutes(1)),
        };
        var service = CreateService(store);

        var result = await service.ListAsync(
            page: 1,
            pageSize: 20,
            jobDefinitionId: DefinitionId,
            search: null,
            isEnabled: null,
            triggerKind: null);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.HasCount(1, result.Value.Items);
        Assert.AreEqual(1, result.Value.Total);
        Assert.AreSame(JobSql.ListSchedulesSqlServer, store.ListStatement);
    }

    [TestMethod]
    public async Task UpdateAsync_RecalculatesNextInstantAndUsesOptimisticVersion()
    {
        var store = new ScheduleStore
        {
            Schedule = ExistingSchedule(
                isEnabled: true,
                nextExecutionAtUtc: Now.AddMinutes(1)),
        };
        var service = CreateService(store);

        var result = await service.UpdateAsync(
            ActorId,
            ScheduleId,
            new UpdateHostJobScheduleRequest(
                JobTriggerKinds.Cron,
                "*/5 * * * *",
                "UTC",
                null,
                JobMisfirePolicies.Skip,
                Version: 3));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual("*/5 * * * *", result.Value.CronExpression);
        Assert.AreEqual(JobMisfirePolicies.Skip, result.Value.MisfirePolicy);
        Assert.AreEqual(Now.AddMinutes(5), result.Value.NextExecutionAtUtc);
        Assert.AreEqual(4, result.Value.Version);
    }

    [TestMethod]
    public async Task ListDefinitionOptionsAsync_ReturnsEnabledDefinitionsOnly()
    {
        var store = new ScheduleStore();
        var service = CreateService(store);

        var result = await service.ListDefinitionOptionsAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.HasCount(1, result.Value);
        Assert.AreEqual(JobsWellKnownKeys.Ping, result.Value[0].JobKey);
    }

    [TestMethod]
    public async Task PreviewCronAsync_ReturnsNextUtcInstant()
    {
        var service = CreateService(new ScheduleStore());

        var result = await service.PreviewCronAsync("0 9 * * *", "UTC");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero),
            result.Value!.NextExecutionAtUtc);
    }

    private static HostJobScheduleService CreateService(ScheduleStore store) =>
        new(
            store,
            store,
            new PassThroughTransaction(),
            new FixedClock(Now),
            new FixedIdGenerator(ScheduleId),
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

    private static JobScheduleDetailRecord ExistingSchedule(
        bool isEnabled,
        DateTimeOffset nextExecutionAtUtc) =>
        new()
        {
            Id = ScheduleId,
            JobDefinitionId = DefinitionId,
            JobDefinitionJobKey = JobsWellKnownKeys.Ping,
            JobDefinitionDisplayName = "Ping",
            TriggerKind = JobTriggerKinds.Cron,
            CronExpression = "* * * * *",
            TimeZoneId = "UTC",
            MisfirePolicy = JobMisfirePolicies.FireOnce,
            IsEnabled = isEnabled,
            NextExecutionAtUtc = nextExecutionAtUtc,
            CreatedAtUtc = Now.AddDays(-2),
            CreatedByUserId = ActorId,
            Version = 3,
        };

    private sealed class ScheduleStore(bool isDefinitionEnabled = true)
        : IQueryExecutor, ICommandExecutor
    {
        public JobScheduleDetailRecord? Schedule { get; set; }

        public int CommandCount { get; private set; }

        public SqlStatement? ListStatement { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            object? value = statement == JobSql.FindDefinitionById
                ? new JobDefinitionRecord
                {
                    Id = DefinitionId,
                    JobKey = JobsWellKnownKeys.Ping,
                    DisplayName = "Ping",
                    IsEnabled = isDefinitionEnabled,
                }
                : statement == JobSql.FindScheduleById
                    ? Schedule
                : statement == JobSql.FindScheduleDetailById
                    ? Schedule
                : statement == JobSql.CountSchedules
                    ? Schedule is null ? 0L : 1L
                : throw new InvalidOperationException(
                    $"Unexpected query '{statement.Name}'.");
            return Task.FromResult((T?)value);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == JobSql.ListSchedulesSqlServer)
            {
                ListStatement = statement;
                return Task.FromResult<IReadOnlyList<T>>(
                    Schedule is null ? [] : [(T)(object)Schedule]);
            }

            if (statement == JobSql.ListEnabledScheduleDefinitionOptions)
            {
                return Task.FromResult<IReadOnlyList<T>>(
                    isDefinitionEnabled
                        ? [(T)(object)new JobDefinitionOptionRecord
                        {
                            Id = DefinitionId,
                            JobKey = JobsWellKnownKeys.Ping,
                            DisplayName = "Ping",
                        }]
                        : []);
            }

            throw new InvalidOperationException(
                $"Unexpected query '{statement.Name}'.");
        }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            var values = ParameterValues(parameters);
            if (statement == JobSql.InsertSchedule)
            {
                Schedule = new JobScheduleDetailRecord
                {
                    Id = (Guid)values[nameof(JobScheduleRecord.Id)]!,
                    JobDefinitionId =
                        (Guid)values[nameof(JobScheduleRecord.JobDefinitionId)]!,
                    JobDefinitionJobKey = JobsWellKnownKeys.Ping,
                    JobDefinitionDisplayName = "Ping",
                    TriggerKind =
                        (string)values[nameof(JobScheduleRecord.TriggerKind)]!,
                    CronExpression =
                        (string?)values[nameof(JobScheduleRecord.CronExpression)],
                    TimeZoneId =
                        (string)values[nameof(JobScheduleRecord.TimeZoneId)]!,
                    MisfirePolicy =
                        (string)values[nameof(JobScheduleRecord.MisfirePolicy)]!,
                    IsEnabled =
                        (bool)values[nameof(JobScheduleRecord.IsEnabled)]!,
                    NextExecutionAtUtc =
                        (DateTimeOffset?)values[
                            nameof(JobScheduleRecord.NextExecutionAtUtc)],
                    CreatedAtUtc =
                        (DateTimeOffset)values[
                            nameof(JobScheduleRecord.CreatedAtUtc)]!,
                    CreatedByUserId =
                        (Guid)values[nameof(JobScheduleRecord.CreatedByUserId)]!,
                    Version = (int)values[nameof(JobScheduleRecord.Version)]!,
                };
                return Task.FromResult(1);
            }

            if (Schedule is null)
            {
                return Task.FromResult(0);
            }

            if (statement == JobSql.PauseSchedule)
            {
                Schedule.IsEnabled = false;
            }
            else if (statement == JobSql.ResumeSchedule)
            {
                Schedule.IsEnabled = true;
                Schedule.NextExecutionAtUtc =
                    (DateTimeOffset?)values[
                        nameof(JobScheduleRecord.NextExecutionAtUtc)];
            }
            else if (statement == JobSql.UpdateSchedule)
            {
                Schedule.TriggerKind =
                    (string)values[nameof(JobScheduleRecord.TriggerKind)]!;
                Schedule.CronExpression =
                    (string?)values[nameof(JobScheduleRecord.CronExpression)];
                Schedule.TimeZoneId =
                    (string)values[nameof(JobScheduleRecord.TimeZoneId)]!;
                Schedule.OneTimeAtUtc =
                    (DateTimeOffset?)values[
                        nameof(JobScheduleRecord.OneTimeAtUtc)];
                Schedule.MisfirePolicy =
                    (string)values[nameof(JobScheduleRecord.MisfirePolicy)]!;
                Schedule.NextExecutionAtUtc =
                    (DateTimeOffset?)values[
                        nameof(JobScheduleRecord.NextExecutionAtUtc)];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected command '{statement.Name}'.");
            }

            Schedule.UpdatedAtUtc =
                (DateTimeOffset)values[nameof(JobScheduleRecord.UpdatedAtUtc)]!;
            Schedule.UpdatedByUserId =
                (Guid)values[nameof(JobScheduleRecord.UpdatedByUserId)]!;
            Schedule.Version = (int)values["NextVersion"]!;
            return Task.FromResult(1);
        }

        private static Dictionary<string, object?> ParameterValues(
            object? parameters) =>
            parameters?.GetType()
                .GetProperties()
                .ToDictionary(
                    property => property.Name,
                    property => property.GetValue(parameters),
                    StringComparer.Ordinal)
            ?? [];
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
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
