using System.Runtime.ExceptionServices;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>领取并执行待处理任务；供 Worker 与集成测试调用。</summary>
internal sealed class JobExecutionRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    JobHandlerRegistry handlerRegistry,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<JobsWorkerOptions> workerOptions,
    ILogger<JobExecutionRunner> logger,
    IServiceScopeFactory? executionScopeFactory = null)
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

            if (processingTask.IsCompletedSuccessfully)
            {
                return await processingTask.ConfigureAwait(false);
            }

            leaseCancellation.Cancel();
            try
            {
                return await processingTask.ConfigureAwait(false);
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
            catch (Exception)
                when (processingTask.IsCompletedSuccessfully)
            {
                // 最后一个执行已写入终态时，零行续租只表示该批次不再需要持有租约。
            }
        }
    }

    private async Task<int> ProcessBatchAsync(
        IReadOnlyList<JobExecutionRecord> acquired,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        var definitions = await queryExecutor.QueryAsync<JobDefinitionRecord>(
                JobSql.FindDefinitionsByIds,
                new
                {
                    Ids = acquired
                        .Select(execution => execution.JobDefinitionId)
                        .Distinct()
                        .ToArray(),
                },
                cancellationToken)
            .ConfigureAwait(false);
        var definitionsById = definitions.ToDictionary(
            definition => definition.Id);
        if (_workerOptions.MaxConcurrency == 1)
        {
            foreach (var execution in acquired)
            {
                await ProcessOneAsync(
                        execution,
                        definitionsById,
                        leaseId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return acquired.Count;
        }

        if (executionScopeFactory is null)
        {
            throw new InvalidOperationException(
                "Jobs parallel execution requires an execution scope factory.");
        }

        var executionGroups = acquired.GroupBy(
            execution => definitionsById.TryGetValue(
                execution.JobDefinitionId,
                out var definition)
                ? definition.JobKey
                : $"missing:{execution.JobDefinitionId:D}",
            StringComparer.Ordinal);
        await Parallel.ForEachAsync(
                executionGroups,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = _workerOptions.MaxConcurrency,
                },
                async (group, groupCancellationToken) =>
                {
                    foreach (var execution in group)
                    {
                        await ProcessOneInScopeAsync(
                                execution,
                                definitionsById,
                                leaseId,
                                groupCancellationToken)
                            .ConfigureAwait(false);
                    }
                })
                .ConfigureAwait(false);

        return acquired.Count;
    }

    private async ValueTask ProcessOneInScopeAsync(
        JobExecutionRecord execution,
        IReadOnlyDictionary<Guid, JobDefinitionRecord> definitionsById,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        await using var scope = executionScopeFactory!.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var currentTenant = services.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            await ProcessOneCoreAsync(
                    execution,
                    definitionsById,
                    leaseId,
                    cancellationToken,
                    services.GetRequiredService<JobHandlerRegistry>(),
                    services.GetRequiredService<ICommandExecutor>(),
                    services.GetRequiredService<IClock>(),
                    logger)
                .ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
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

    private Task ProcessOneAsync(
        JobExecutionRecord execution,
        IReadOnlyDictionary<Guid, JobDefinitionRecord> definitionsById,
        Guid leaseId,
        CancellationToken cancellationToken) =>
        ProcessOneCoreAsync(
            execution,
            definitionsById,
            leaseId,
            cancellationToken,
            handlerRegistry,
            commandExecutor,
            clock,
            logger);

    private static async Task ProcessOneCoreAsync(
        JobExecutionRecord execution,
        IReadOnlyDictionary<Guid, JobDefinitionRecord> definitionsById,
        Guid leaseId,
        CancellationToken cancellationToken,
        JobHandlerRegistry scopedHandlerRegistry,
        ICommandExecutor scopedCommandExecutor,
        IClock scopedClock,
        ILogger<JobExecutionRunner> scopedLogger)
    {
        if (!definitionsById.TryGetValue(
                execution.JobDefinitionId,
                out var definition)
            || !scopedHandlerRegistry.TryGetHandler(
                definition.JobKey,
                out var handler)
            || handler is null)
        {
            await MarkFailedAsync(
                    execution.Id,
                    leaseId,
                    "Job handler was not found.",
                    cancellationToken,
                    scopedCommandExecutor,
                    scopedClock)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await handler.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            await MarkSucceededAsync(
                    execution.Id,
                    leaseId,
                    cancellationToken,
                    scopedCommandExecutor,
                    scopedClock)
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
                scopedLogger,
                exception,
                execution.Id,
                definition.JobKey);
            await MarkFailedAsync(
                    execution.Id,
                    leaseId,
                    exception.Message,
                    cancellationToken,
                    scopedCommandExecutor,
                    scopedClock)
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
            return await transaction.ExecuteAsync(
                    async transactionCancellationToken =>
                    {
                        // 先锁定候选行并跳过其他 Worker 持有的锁，再按主键更新，避免领取与终态更新发生反向等待。
                        var ids = await queryExecutor.QueryAsync<Guid>(
                                JobSql.SelectClaimableExecutionIdsMySql,
                                parameters,
                                transactionCancellationToken)
                            .ConfigureAwait(false);
                        if (ids.Count == 0)
                        {
                            return Array.Empty<JobExecutionRecord>();
                        }

                        await commandExecutor.ExecuteAsync(
                                JobSql.ClaimExecutionsByIdsMySql,
                                new
                                {
                                    Ids = ids.ToArray(),
                                    LeaseId = leaseId,
                                    Now = now,
                                    LeaseExpiresAtUtc = leaseExpiresAt,
                                    RunningStatus = JobExecutionStatuses.Running,
                                },
                                transactionCancellationToken)
                            .ConfigureAwait(false);
                        var rows = await queryExecutor.QueryAsync<JobExecutionRecord>(
                                JobSql.SelectExecutionsByLeaseMySql,
                                new { LeaseId = leaseId },
                                transactionCancellationToken)
                            .ConfigureAwait(false);
                        return rows.ToArray();
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Unsupported database provider '{databaseOptions.Value.Provider}'.");
    }

    private static Task MarkSucceededAsync(
        Guid executionId,
        Guid leaseId,
        CancellationToken cancellationToken,
        ICommandExecutor scopedCommandExecutor,
        IClock scopedClock) =>
        scopedCommandExecutor.ExecuteAsync(
            JobSql.MarkExecutionSucceeded,
            new
            {
                Id = executionId,
                LeaseId = leaseId,
                RunningStatus = JobExecutionStatuses.Running,
                SucceededStatus = JobExecutionStatuses.Succeeded,
                FinishedAtUtc = scopedClock.UtcNow,
            },
            cancellationToken);

    private static Task MarkFailedAsync(
        Guid executionId,
        Guid leaseId,
        string errorMessage,
        CancellationToken cancellationToken,
        ICommandExecutor scopedCommandExecutor,
        IClock scopedClock) =>
        scopedCommandExecutor.ExecuteAsync(
            JobSql.MarkExecutionFailed,
            new
            {
                Id = executionId,
                LeaseId = leaseId,
                RunningStatus = JobExecutionStatuses.Running,
                FailedStatus = JobExecutionStatuses.Failed,
                FinishedAtUtc = scopedClock.UtcNow,
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
