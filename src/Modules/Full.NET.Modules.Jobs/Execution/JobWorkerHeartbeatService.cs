using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>维护当前 Worker 进程在数据库中的心跳记录。</summary>
internal sealed class JobWorkerHeartbeatService(
    ICommandExecutor commandExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<JobsWorkerOptions> workerOptions)
{
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
        await commandExecutor.ExecuteAsync(
                statement,
                new
                {
                    InstanceId = _instanceId,
                    HostProfile = Environment.MachineName,
                    StartedAtUtc = _startedAtUtc,
                    LastHeartbeatAtUtc = now,
                    WorkerVersion = typeof(JobWorkerHeartbeatService).Assembly
                        .GetName()
                        .Version?
                        .ToString(),
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public TimeSpan StaleThreshold =>
        TimeSpan.FromMilliseconds(workerOptions.Value.PollMilliseconds * 2);
}
