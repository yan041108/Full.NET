using System.Runtime.ExceptionServices;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>领取并执行待处理任务；供 Worker 与集成测试调用。</summary>
internal sealed class JobExecutionRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    JobHandlerRegistry handlerRegistry,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<JobsWorkerOptions> workerOptions,
    ILogger<JobExecutionRunner> logger)
{
    private readonly JobsWorkerOptions _workerOptions = workerOptions.Value;

    public async Task<int> ProcessPendingAsync(
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        batchSize = Math.Clamp(batchSize, 1, 50);
        var leaseId = idGenerator.NewId();
        var now = clock.UtcNow;
        var leaseExpiresAt = now.AddSeconds(_workerOptions.LeaseSeconds);
        var acquired = await AcquireAsync(
                batchSize,
                leaseId,
                now,
                leaseExpiresAt,
                cancellationToken)
            .ConfigureAwait(false);
        if (acquired.Count == 0)
        {
            return 0;
        }

        using var leaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingTask = ProcessBatchAsync(
            acquired,
            leaseId,
            leaseCancellation.Token);
        var renewalTask = RenewLeaseUntilCanceledAsync(
            leaseId,
            leaseCancellation.Token);
        var completedTask = await Task.WhenAny(processingTask, renewalTask)
            .ConfigureAwait(false);
        if (completedTask == renewalTask)
        {
            Exception? renewalFailure = null;
            try
            {
                await renewalTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                renewalFailure = exception;
            }

            leaseCancellation.Cancel();
            try
            {
                await processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (leaseCancellation.IsCancellationRequested)
            {
                // 续租失败后先等待协作式 Handler 退出，再传播原始租约故障。
            }

            if (renewalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(renewalFailure).Throw();
            }

            throw new InvalidOperationException(
                $"Job execution lease '{leaseId:D}' renewal stopped unexpectedly.");
        }

        try
        {
            return await processingTask.ConfigureAwait(false);
        }
        finally
        {
            leaseCancellation.Cancel();
            try
            {
                await renewalTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (leaseCancellation.IsCancellationRequested)
            {
                // 批次已经结束或宿主正在退出，续租循环应随 linked token 有界停止。
            }
        }
    }

    private async Task<int> ProcessBatchAsync(
        IReadOnlyList<JobExecutionRecord> acquired,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        foreach (var execution in acquired)
        {
            await ProcessOneAsync(execution, leaseId, cancellationToken)
                .ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    private async Task RenewLeaseUntilCanceledAsync(
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        var renewalInterval = TimeSpan.FromSeconds(
            _workerOptions.LeaseRenewalSeconds);
        var leaseDuration = TimeSpan.FromSeconds(_workerOptions.LeaseSeconds);
        while (true)
        {
            await Task.Delay(renewalInterval, cancellationToken)
                .ConfigureAwait(false);
            var renewed = await commandExecutor.ExecuteAsync(
                    JobSql.RenewExecutionLease,
                    new
                    {
                        LeaseId = leaseId,
                        LeaseExpiresAtUtc = clock.UtcNow.Add(leaseDuration),
                        RunningStatus = JobExecutionStatuses.Running,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (renewed == 0)
            {
                throw new InvalidOperationException(
                    $"Job execution lease '{leaseId:D}' is no longer owned.");
            }
        }
    }

    private async Task ProcessOneAsync(
        JobExecutionRecord execution,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        var definition = await queryExecutor.QuerySingleOrDefaultAsync<JobDefinitionRecord>(
                JobSql.FindDefinitionById,
                new { Id = execution.JobDefinitionId },
                cancellationToken)
            .ConfigureAwait(false);
        if (definition is null
            || !handlerRegistry.TryGetHandler(definition.JobKey, out var handler)
            || handler is null)
        {
            await MarkFailedAsync(
                    execution.Id,
                    leaseId,
                    "Job handler was not found.",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await handler.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            await MarkSucceededAsync(execution.Id, leaseId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // 宿主取消不属于业务失败；保留当前租约，由过期恢复路径重新领取未完成任务。
            throw;
        }
        catch (Exception exception)
        {
            JobExecutionRunnerLog.ExecutionFailed(
                logger,
                exception,
                execution.Id,
                definition.JobKey);
            await MarkFailedAsync(
                    execution.Id,
                    leaseId,
                    exception.Message,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<JobExecutionRecord>> AcquireAsync(
        int batchSize,
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            BatchSize = batchSize,
            LeaseId = leaseId,
            Now = now,
            LeaseExpiresAtUtc = leaseExpiresAt,
            PendingStatus = JobExecutionStatuses.Pending,
            RunningStatus = JobExecutionStatuses.Running,
        };

        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            var rows = await queryExecutor.QueryAsync<JobExecutionRecord>(
                    JobSql.AcquireExecutionsSqlServer,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            return rows.ToArray();
        }

        if (databaseOptions.Value.Provider == DatabaseProvider.MySql)
        {
            await commandExecutor.ExecuteAsync(
                    JobSql.AcquireExecutionsMySql,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            var rows = await queryExecutor.QueryAsync<JobExecutionRecord>(
                    JobSql.SelectExecutionsByLeaseMySql,
                    new { LeaseId = leaseId },
                    cancellationToken)
                .ConfigureAwait(false);
            return rows.ToArray();
        }

        throw new InvalidOperationException(
            $"Unsupported database provider '{databaseOptions.Value.Provider}'.");
    }

    private Task MarkSucceededAsync(
        Guid executionId,
        Guid leaseId,
        CancellationToken cancellationToken) =>
        commandExecutor.ExecuteAsync(
            JobSql.MarkExecutionSucceeded,
            new
            {
                Id = executionId,
                LeaseId = leaseId,
                RunningStatus = JobExecutionStatuses.Running,
                SucceededStatus = JobExecutionStatuses.Succeeded,
                FinishedAtUtc = clock.UtcNow,
            },
            cancellationToken);

    private Task MarkFailedAsync(
        Guid executionId,
        Guid leaseId,
        string errorMessage,
        CancellationToken cancellationToken) =>
        commandExecutor.ExecuteAsync(
            JobSql.MarkExecutionFailed,
            new
            {
                Id = executionId,
                LeaseId = leaseId,
                RunningStatus = JobExecutionStatuses.Running,
                FailedStatus = JobExecutionStatuses.Failed,
                FinishedAtUtc = clock.UtcNow,
                ErrorMessage = errorMessage.Length > 2000
                    ? errorMessage[..2000]
                    : errorMessage,
            },
            cancellationToken);
}

internal static partial class JobExecutionRunnerLog
{
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "Job execution {ExecutionId} ({JobKey}) failed")]
    public static partial void ExecutionFailed(
        ILogger logger,
        Exception exception,
        Guid executionId,
        string jobKey);
}
