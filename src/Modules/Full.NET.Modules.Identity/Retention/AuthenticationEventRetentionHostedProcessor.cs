using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Retention;

/// <summary>Worker 专属认证审计清理循环，配置关闭后不再启动新批次。</summary>
internal sealed class AuthenticationEventRetentionHostedProcessor(
    IServiceScopeFactory scopes,
    IOptionsMonitor<AuthenticationEventRetentionOptions> options,
    ILogger<AuthenticationEventRetentionHostedProcessor> logger) : BackgroundService
{
    private static readonly Meter Meter = new("Full.NET.Identity.AuthenticationEventRetention");
    private static readonly Counter<long> Deleted = Meter.CreateCounter<long>("deleted");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("failures");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.CurrentValue.Enabled)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                    tenant.SetHost();
                    try
                    {
                        var result = await scope.ServiceProvider
                            .GetRequiredService<AuthenticationEventRetentionRunner>()
                            .RunOnceAsync(options.CurrentValue, stoppingToken)
                            .ConfigureAwait(false);
                        Deleted.Add(result.Deleted);
                        AuthenticationEventRetentionLog.IterationCompleted(
                            logger, result.Deleted, result.Batches);
                    }
                    finally
                    {
                        tenant.Clear();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Failures.Add(1);
                AuthenticationEventRetentionLog.IterationFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.PollSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }
}

internal static partial class AuthenticationEventRetentionLog
{
    [LoggerMessage(EventId = 4521, Level = LogLevel.Information,
        Message = "Authentication event retention deleted {Deleted} rows in {Batches} batches")]
    public static partial void IterationCompleted(ILogger logger, int deleted, int batches);

    [LoggerMessage(EventId = 4522, Level = LogLevel.Error,
        Message = "Authentication event retention iteration failed")]
    public static partial void IterationFailed(ILogger logger, Exception exception);
}
