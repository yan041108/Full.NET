using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiAgentRunReadinessTests
{
    [TestMethod]
    public async Task Disabled_runtime_rejects_new_runs()
    {
        var readiness = CreateReadiness(accepts: false, fresh: true);
        Assert.IsFalse(await readiness.CanAcceptNewRunsAsync());
    }

    [TestMethod]
    public async Task Stale_worker_heartbeat_rejects_new_runs()
    {
        var readiness = CreateReadiness(accepts: true, fresh: false);
        Assert.IsFalse(await readiness.CanAcceptNewRunsAsync());
    }

    [TestMethod]
    public async Task Fresh_worker_and_enabled_runtime_accepts_new_runs()
    {
        var readiness = CreateReadiness(accepts: true, fresh: true);
        Assert.IsTrue(await readiness.CanAcceptNewRunsAsync());
    }

    private static AiAgentRunReadiness CreateReadiness(bool accepts, bool fresh)
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<int>(
                AiAgentWorkerSql.HasFreshWorker,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(fresh ? 1 : 0);
        var heartbeat = new AiAgentWorkerHeartbeatService(
            Substitute.For<IServiceScopeFactory>(),
            new FixedClock(DateTimeOffset.UtcNow),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new AiAgentRuntimeOptions { AcceptsNewRuns = accepts, RuntimeVersion = "1", PollMilliseconds = 1000 }));
        return new AiAgentRunReadiness(
            queries,
            new FixedClock(DateTimeOffset.UtcNow),
            Options.Create(new AiAgentRuntimeOptions { AcceptsNewRuns = accepts, RuntimeVersion = "1" }),
            heartbeat);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
