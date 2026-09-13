using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>维护 AI Agent Worker 实例心跳，供就绪门禁与观测使用。</summary>
internal sealed class AiAgentWorkerHeartbeatService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<AiAgentRuntimeOptions> runtimeOptions)
{
    private readonly Guid _instanceId = Guid.CreateVersion7();
    private readonly DateTimeOffset _startedAtUtc = clock.UtcNow;

    public Guid InstanceId => _instanceId;

    public string WorkerId => _instanceId.ToString("N");

    public TimeSpan StaleThreshold => TimeSpan.FromMilliseconds(runtimeOptions.Value.PollMilliseconds * 3);

    public async Task UpsertAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AiAgentWorkerSql.UpsertSqlServer,
            DatabaseProvider.MySql => AiAgentWorkerSql.UpsertMySql,
            _ => throw new InvalidOperationException($"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantWriter = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenantWriter.SetHost();
        try
        {
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(
                statement,
                AiSqlParameters.Create(
                    ("InstanceId", _instanceId),
                    ("WorkerRole", AiAgentWorkerSql.WorkerRole),
                    ("RuntimeVersion", runtimeOptions.Value.RuntimeVersion),
                    ("HostProfile", Environment.MachineName),
                    ("StartedAtUtc", _startedAtUtc),
                    ("LastHeartbeatAtUtc", now)),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            tenantWriter.Clear();
        }
    }
}
