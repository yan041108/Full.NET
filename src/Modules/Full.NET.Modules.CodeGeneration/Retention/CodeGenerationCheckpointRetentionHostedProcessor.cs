using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.CodeGeneration.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Retention;

/// <summary>
/// Worker 后台服务：按 PollSeconds 周期执行检查点保留清理；仅在 Apply 与本服务同时启用时生效，循环内吞掉异常以保持运行。
/// </summary>
internal sealed class CodeGenerationCheckpointRetentionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<CodeGenerationCheckpointRetentionOptions> options,
    ILogger<CodeGenerationCheckpointRetentionHostedProcessor> logger)
    : BackgroundService
{
    /// <summary>
    /// 循环执行清理并在每轮之间等待 PollSeconds；单轮异常被记录后继续，避免一次性故障终止整个后台服务。
    /// </summary>
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

    /// <summary>
    /// 在独立作用域内执行一次清理，并显式将当前租户上下文设置为 Host；清理 SQL 为 Global 作用域，必须在此 Host 上下文内执行，结束时清除上下文避免泄漏到其他工作。
    /// </summary>
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