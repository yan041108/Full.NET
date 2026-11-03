using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>Worker 重申已提交控制目标，处理 Connect 不支持 fencing token 的有限迟到动作。</summary>
internal sealed partial class KafkaDeliveryControlReconciler(IServiceScopeFactory scopes,
    IOptions<KafkaConnectRollbackOptions> options, IOptions<KafkaMessagingOptions> kafka,
    KafkaConnectEventDeliveryRollbackReadinessReader reader, ILogger<KafkaDeliveryControlReconciler> logger) : BackgroundService
{
    private int _nextStream;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !kafka.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.PrepareTimeoutSeconds));
                var streams = options.Value.Streams;
                var first = _nextStream; _nextStream = (first + 1) % streams.Length;
                // 发现偏差后立即处理；不能等整轮扫描完成才取得锁，否则尾部慢流会耗尽已有待处理项的期限。
                await RunRoundAsync(streams, first, async (stream, token) =>
                {
                    if (!await reader.NeedsReconciliationAsync(stream.EventType, stream.SchemaVersion, token).ConfigureAwait(false)) return;
                    await using var scope = scopes.CreateAsyncScope();
                    await using var handle = await scope.ServiceProvider.GetRequiredService<IDatabaseSessionLock>()
                        .TryAcquireAsync(EventDeliveryControlResources.SessionLock, token).ConfigureAwait(false);
                    if (handle is null) return;
                    var lease = new EventDeliveryControlLease(leaseToken => (handle as IDatabaseSessionLockLease
                        ?? throw new InvalidOperationException("控制会话不支持持锁校验。")).EnsureHeldAsync(leaseToken));
                    if (await reader.ReconcileAsync(stream.EventType, stream.SchemaVersion, lease, token).ConfigureAwait(false)) Corrected(logger);
                }, ReportUnavailable, deadline.Token, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                ReportUnavailable(exception);
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        }
    }

    internal static async Task RunRoundAsync(KafkaConnectRollbackStreamBinding[] streams, int first,
        Func<KafkaConnectRollbackStreamBinding, CancellationToken, Task> process, Action<Exception> report,
        CancellationToken deadline, CancellationToken stopping)
    {
        foreach (var stream in streams.Skip(first).Concat(streams.Take(first)))
        {
            try { await process(stream, deadline).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
            catch (Exception exception) { report(exception); }
            if (deadline.IsCancellationRequested) break;
        }
    }

    private void ReportUnavailable(Exception exception)
    {
        // 只报稳定诊断，不能把配置、资源地址或数据库异常内容写入日志，也不修改持久保护。
        var reasonCode = exception switch
        {
            KafkaControlReconciliationException control => control.ReasonCode,
            OperationCanceledException => "timeout",
            System.Data.Common.DbException => "database_unavailable",
            HttpRequestException => "control_unavailable",
            _ => "execution_unavailable",
        };
        Unavailable(logger, reasonCode);
    }

    [LoggerMessage(1480, LogLevel.Warning, "Messaging control target was reconciled from durable terminal state.")]
    private static partial void Corrected(ILogger logger);

    [LoggerMessage(1481, LogLevel.Warning, "Messaging control reconciliation is unavailable ({ReasonCode}); persisted ownership and producer protection are retained.")]
    private static partial void Unavailable(ILogger logger, string reasonCode);
}
