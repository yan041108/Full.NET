using System.Diagnostics;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

internal sealed class AuditingRetentionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AuditingRetentionOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    ILogger<AuditingRetentionHostedProcessor> logger) : BackgroundService
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
                AuditingRetentionHostedProcessorLog.IterationFailed(
                    logger,
                    exception);
            }

            var delay = TimeSpan.FromSeconds(options.CurrentValue.PollSeconds);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<AuditingRetentionResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return AuditingRetentionResult.Empty;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var currentTenant = scope.ServiceProvider
                .GetRequiredService<ICurrentTenantContextWriter>();
            currentTenant.SetHost();
            try
            {
                var result = await scope.ServiceProvider
                    .GetRequiredService<AuditingRetentionRunner>()
                    .RunOnceAsync(currentOptions, cancellationToken)
                    .ConfigureAwait(false);
                AuditingRetentionTelemetry.RecordSuccess(
                    result,
                    _provider,
                    Stopwatch.GetElapsedTime(started),
                    clock.UtcNow);
                AuditingRetentionHostedProcessorLog.IterationCompleted(
                    logger,
                    result.TotalDeleted,
                    result.BatchesExecuted);
                return result;
            }
            finally
            {
                currentTenant.Clear();
            }
        }
        catch
        {
            AuditingRetentionTelemetry.RecordFailure(
                _provider,
                Stopwatch.GetElapsedTime(started));
            throw;
        }
    }
}

internal static partial class AuditingRetentionHostedProcessorLog
{
    [LoggerMessage(
        EventId = 4401,
        Level = LogLevel.Information,
        Message = "Audit retention deleted {DeletedRows} rows in {Batches} batches")]
    public static partial void IterationCompleted(
        ILogger logger,
        int deletedRows,
        int batches);

    [LoggerMessage(
        EventId = 4402,
        Level = LogLevel.Error,
        Message = "Audit retention iteration failed")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception);
}
