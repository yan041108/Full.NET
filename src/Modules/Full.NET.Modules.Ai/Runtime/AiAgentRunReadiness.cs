using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>新 Run 准入：配置开启且存在匹配版本的新鲜 Worker 心跳。</summary>
internal sealed class AiAgentRunReadiness(
    IQueryExecutor queries,
    IClock clock,
    IOptions<AiAgentRuntimeOptions> runtimeOptions,
    AiAgentWorkerHeartbeatService heartbeat)
{
    public async Task<bool> CanAcceptNewRunsAsync(CancellationToken cancellationToken = default)
    {
        var options = runtimeOptions.Value;
        if (!options.AcceptsNewRuns || string.IsNullOrWhiteSpace(options.RuntimeVersion))
        {
            return false;
        }

        var staleBefore = clock.UtcNow - heartbeat.StaleThreshold;
        var fresh = await queries.QuerySingleOrDefaultAsync<int>(
            AiAgentWorkerSql.HasFreshWorker,
            AiSqlParameters.Create(
                ("WorkerRole", AiAgentWorkerSql.WorkerRole),
                ("RuntimeVersion", options.RuntimeVersion),
                ("StaleBefore", staleBefore)),
            cancellationToken).ConfigureAwait(false);
        return fresh == 1;
    }
}
