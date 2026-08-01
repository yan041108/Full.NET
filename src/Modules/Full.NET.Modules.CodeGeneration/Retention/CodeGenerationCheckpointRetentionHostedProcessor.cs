using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.CodeGeneration.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Retention;

internal sealed class CodeGenerationCheckpointRetentionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<CodeGenerationCheckpointRetentionOptions> options,
    ILogger<CodeGenerationCheckpointRetentionHostedProcessor> logger)
    : BackgroundService
{
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
                CodeGenerationCheckpointRetentionHostedProcessorLog.IterationFailed(
                    logger,
                    exception);
            }

            var delay = TimeSpan.FromSeconds(options.CurrentValue.PollSeconds);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<CodeGenerationCheckpointRetentionResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return CodeGenerationCheckpointRetentionResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<CodeGenerationCheckpointRetentionRunner>()
                .RunOnceAsync(currentOptions, cancellationToken)
                .ConfigureAwait(false);
            CodeGenerationCheckpointRetentionHostedProcessorLog.IterationCompleted(
                logger,
                result.Scanned,
                result.Deleted,
                result.Skipped,
                result.Failed);
            return result;
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}

internal static partial class CodeGenerationCheckpointRetentionHostedProcessorLog
{
    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Information,
        Message = "Code generation checkpoint retention scanned {Scanned}, deleted {Deleted}, skipped {Skipped}, failed {Failed}")]
    public static partial void IterationCompleted(
        ILogger logger,
        int scanned,
        int deleted,
        int skipped,
        int failed);

    [LoggerMessage(
        EventId = 5202,
        Level = LogLevel.Error,
        Message = "Code generation checkpoint retention iteration failed")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception);
}