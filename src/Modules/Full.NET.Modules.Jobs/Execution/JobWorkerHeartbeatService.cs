using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>维护当前 Worker 进程在数据库中的心跳记录。</summary>
internal sealed class JobWorkerHeartbeatService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<JobsWorkerOptions> workerOptions)
{
    private long _nextCleanupTicks;
    private readonly Guid _instanceId = Guid.CreateVersion7();
    private readonly DateTimeOffset _startedAtUtc = clock.UtcNow.ToUniversalTime();

    public async Task UpsertAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow.ToUniversalTime();
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => JobSql.UpsertWorkerHeartbeat,
            DatabaseProvider.MySql => JobSql.UpsertWorkerHeartbeatMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var commandExecutor = scope.ServiceProvider
                .GetRequiredService<ICommandExecutor>();
            await commandExecutor.ExecuteAsync(
                    statement,
                    JobsSqlParameters.Create(
                        ("InstanceId", _instanceId),
                        ("HostProfile", Environment.MachineName),
                        ("StartedAtUtc", _startedAtUtc),
                        ("LastHeartbeatAtUtc", now),
                        ("WorkerVersion", typeof(JobWorkerHeartbeatService).Assembly
                            .GetName()
                            .Version?
                            .ToString())
                    ),
                    cancellationToken)
                .ConfigureAwait(false);
            var next = Interlocked.Read(ref _nextCleanupTicks);
            if (now.UtcTicks >= next && Interlocked.CompareExchange(ref _nextCleanupTicks, now.AddMinutes(1).UtcTicks, next) == next)
            {
                // 心跳可随进程重启产生新 Id；历史实例仅保留一天，每轮清理一小批。
                await commandExecutor.ExecuteAsync(databaseOptions.Value.Provider == DatabaseProvider.MySql
                        ? JobSql.DeleteExpiredWorkerInstancesMySql : JobSql.DeleteExpiredWorkerInstances,
                    JobsSqlParameters.Create(("CutoffUtc", now.AddHours(-24))), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    public TimeSpan StaleThreshold =>
        TimeSpan.FromMilliseconds(workerOptions.Value.PollMilliseconds * 2);
}
