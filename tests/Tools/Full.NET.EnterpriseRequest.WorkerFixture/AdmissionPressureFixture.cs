using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

/// <summary>只在独立进程测试中占用普通许可，保留正式后台循环。</summary>
internal static class AdmissionPressureFixture
{
    internal static bool TryRegister(IServiceCollection services, IConfiguration configuration, HashSet<ServiceDescriptor> existing)
    {
        var mode = configuration["RecoveryFixture:Pause"];
        if (mode is not ("admission-pressure" or "admission-observer")) return false;
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && !existing.Contains(d)
            && d.ImplementationType?.Name is not ("JobExecutionHostedProcessor"
                or "WorkflowRecoveryHostedProcessor" or "NotificationDeliveryHostedProcessor")).ToArray())
            services.Remove(descriptor);
        if (mode == "admission-pressure")
        {
            var gate = configuration["RecoveryFixture:GateFile"] ?? throw new InvalidOperationException("缺少准入验收闸门。");
            // 普通许可在正式轮询启动前占满，后台工作仍从原 DI 路径进入。
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(sp => new Pressure(
                sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IOptions<DatabaseCapacityOptions>>().Value, gate)));
        }
        return true;
    }

    private sealed class Pressure(IServiceScopeFactory scopes, DatabaseCapacityOptions options, string gate) : IHostedService, IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MeterListener _listener = new();
        private int _count;
        private int _rejected;
        private int _timedOut;
        private Task _running = Task.CompletedTask;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!options.Enabled || options.PermitLimit != 7 || options.QueueLimit != 1 || options.CriticalWorkerReserve != 1)
                throw new InvalidOperationException("准入验收必须使用已冻结的 Worker 配额。");
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name == "fullnet.db.connection.acquire") listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key != "outcome") continue;
                    if (tag.Value is "rejected" && Interlocked.Exchange(ref _rejected, 1) == 0)
                        File.WriteAllText(gate + ".rejected", string.Empty);
                    if (tag.Value is "timeout" && Interlocked.Exchange(ref _timedOut, 1) == 0)
                        File.WriteAllText(gate + ".timeout", string.Empty);
                }
            });
            _listener.Start();
            _running = HoldUntilReleasedAsync(_stop.Token);
            if (await Task.WhenAny(_held.Task, _running).WaitAsync(cancellationToken) == _running) await _running;
        }

        private async Task HoldUntilReleasedAsync(CancellationToken cancellationToken)
        {
            var holders = Enumerable.Range(0, options.PermitLimit).Select(_ => HoldAsync(cancellationToken)).ToArray();
            var all = Task.WhenAll(holders);
            try
            {
                var firstHolder = Task.WhenAny(holders);
                if (await Task.WhenAny(_held.Task, firstHolder).WaitAsync(cancellationToken) == firstHolder)
                    await await firstHolder;
                while (!File.Exists(gate)) await Task.Delay(50, cancellationToken);
            }
            finally
            {
                _release.TrySetResult();
                await all;
            }
            File.WriteAllText(gate + ".released", string.Empty);
        }

        private async Task HoldAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token =>
            {
                if (Interlocked.Increment(ref _count) == options.PermitLimit) _held.TrySetResult();
                await _release.Task.WaitAsync(token);
                return true;
            }, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _stop.CancelAsync();
            try { await _running.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public void Dispose()
        {
            _listener.Dispose();
            _stop.Dispose();
        }
    }
}
