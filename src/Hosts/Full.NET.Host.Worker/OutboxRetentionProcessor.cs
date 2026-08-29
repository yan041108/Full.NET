using System.Diagnostics;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

internal sealed record OutboxRetentionResult(
    int DeletedRows,
    int BatchesExecuted)
{
    public static OutboxRetentionResult Empty { get; } = new(0, 0);
}

internal sealed class OutboxRetentionProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<OutboxRetentionOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    ILogger<OutboxRetentionProcessor> logger) : BackgroundService
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                OutboxRetentionProcessorLog.IterationFailed(logger, exception);
            }

            var delay = TimeSpan.FromSeconds(options.CurrentValue.PollSeconds);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<OutboxRetentionResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return OutboxRetentionResult.Empty;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var cutoffUtc = clock.UtcNow.AddDays(-currentOptions.RetentionDays);
            var deletedRows = 0;
            var batchesExecuted = 0;
            await using var scope = scopeFactory.CreateAsyncScope();
            var currentTenant = scope.ServiceProvider
                .GetRequiredService<ICurrentTenantContextWriter>();
            currentTenant.SetHost();
            try
            {
                var store = scope.ServiceProvider
                    .GetRequiredService<IOutboxRetentionStore>();
                while (batchesExecuted < currentOptions.MaxBatchesPerRun)
                {
                    // 每个新批次前重新读取开关，使配置热更新可以暂停继续删除。
                    if (!options.CurrentValue.Enabled)
                    {
                        break;
                    }

                    var batchDeleted = await store.DeleteProcessedBatchAsync(
                            cutoffUtc,
                            currentOptions.BatchSize,
                            cancellationToken)
                        .ConfigureAwait(false);
                    deletedRows += batchDeleted;
                    batchesExecuted++;
                    if (batchDeleted < currentOptions.BatchSize)
                    {
                        break;
                    }
                }
            }
            finally
            {
                currentTenant.Clear();
            }

            var result = new OutboxRetentionResult(
                deletedRows,
                batchesExecuted);
            OutboxRetentionTelemetry.RecordSuccess(
                result,
                _provider,
                Stopwatch.GetElapsedTime(started),
                clock.UtcNow);
            OutboxRetentionProcessorLog.IterationCompleted(
                logger,
                result.DeletedRows,
                result.BatchesExecuted);
            return result;
        }
        catch
        {
            OutboxRetentionTelemetry.RecordFailure(
                _provider,
                Stopwatch.GetElapsedTime(started));
            throw;
        }
    }
}

internal static partial class OutboxRetentionProcessorLog
{
    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Information,
        Message = "Outbox retention deleted {DeletedRows} rows in {Batches} batches")]
    public static partial void IterationCompleted(
        ILogger logger,
        int deletedRows,
        int batches);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Error,
        Message = "Outbox retention iteration failed")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception);
}
