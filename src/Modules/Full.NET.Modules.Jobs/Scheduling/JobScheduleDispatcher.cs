using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Scheduling;

/// <summary>在一个数据库事务中把到期计划转换为执行记录并推进计划游标。</summary>
internal sealed class JobScheduleDispatcher(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<int> ProcessDueAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ProcessDueCoreAsync(batchSize, token),
            cancellationToken);

    private async Task<int> ProcessDueCoreAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var now = clock.UtcNow.ToUniversalTime();
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => JobSql.SelectDueSchedulesSqlServer,
            DatabaseProvider.MySql => JobSql.SelectDueSchedulesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var schedules = await queryExecutor.QueryAsync<JobScheduleRecord>(
                statement,
                new { BatchSize = batchSize, Now = now },
                cancellationToken)
            .ConfigureAwait(false);

        var createdCount = 0;
        foreach (var schedule in schedules)
        {
            var decision = JobScheduleCalculator.CalculateDue(schedule, now);
            if (decision.CreateExecution)
            {
                await commandExecutor.ExecuteAsync(
                        JobSql.InsertScheduledExecution,
                        new
                        {
                            Id = idGenerator.NewId(),
                            schedule.JobDefinitionId,
                            JobScheduleId = schedule.Id,
                            Status = JobExecutionStatuses.Pending,
                            schedule.TriggerKind,
                            ScheduledForUtc = decision.ScheduledForUtc,
                            CreatedAtUtc = now,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                createdCount++;
            }

            var affected = await commandExecutor.ExecuteAsync(
                    JobSql.AdvanceSchedule,
                    new
                    {
                        schedule.Id,
                        IsEnabled = decision.CompletedAtUtc is null,
                        decision.NextExecutionAtUtc,
                        LastExecutionAtUtc = decision.CreateExecution
                            ? now
                            : (DateTimeOffset?)null,
                        decision.CompletedAtUtc,
                        UpdatedAtUtc = now,
                        NextVersion = schedule.Version + 1,
                        schedule.Version,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                throw new InvalidOperationException(
                    "The locked job schedule was not advanced exactly once.");
            }
        }

        return createdCount;
    }
}
