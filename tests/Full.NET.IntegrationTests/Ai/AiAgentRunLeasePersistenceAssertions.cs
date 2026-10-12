using System.Data.Common;
using System.Diagnostics;
using System.Collections.Concurrent;
using Dapper;
using Full.NET.Agents.Runtime;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产 IAgentRunStore 与双库租约 SQL 验证并发领取、fencing 与到期重领。</summary>
internal static class AiAgentRunLeasePersistenceAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        await Concurrent_claim_admits_only_one_owner_async(factory).ConfigureAwait(false);
        await Expired_lease_can_be_reclaimed_by_another_worker_async(factory).ConfigureAwait(false);
        await Stale_lease_cannot_commit_terminal_progress_async(factory).ConfigureAwait(false);
        await Renew_requires_current_version_async(factory).ConfigureAwait(false);
        await Live_renewals_handoff_versions_and_commit_async(factory).ConfigureAwait(false);
        await Expired_authority_cannot_renew_or_commit_async(factory, deadline: false).ConfigureAwait(false);
        await Expired_authority_cannot_renew_or_commit_async(factory, deadline: true).ConfigureAwait(false);
        await Terminal_run_cannot_be_rewritten_async(factory).ConfigureAwait(false);
        await Step_trace_survives_worker_handoff_async(factory).ConfigureAwait(false);
    }

    /// <summary>不同 Worker 的 Trace 不强制续接；通过已存 RunId/StepId 关联，旧租约不能追加步骤。</summary>
    private static async Task Step_trace_survives_worker_handoff_async(FullNetApiFactory factory)
    {
        var runId = await SeedQueuedRunAsync(factory, DateTimeOffset.UtcNow);
        var lease = (await AcquireAsync(factory, runId, "trace-worker-a", DateTimeOffset.UtcNow))!;
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentExecutionTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { if (Equals(activity.GetTagItem("ai.run.id"), runId.ToString("D"))) spans.Enqueue(activity); }
        };
        ActivitySource.AddActivityListener(listener);
        var first = new AgentStepUpsert(Guid.CreateVersion7(), "trace.first", 1, runId, "committed", 1,
            null, null, 7, 3, null, null, null, null, null);
        string firstTrace;
        using (var root = new Activity("trace.first.worker").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            Assert.IsTrue(await UseStoreAsync(factory, async store => await store.CommitProgressAsync(new(lease, "running", first, null, null))));
            firstTrace = root.TraceId.ToHexString();
        }
        await ExpireLeaseAsync(factory, runId, DateTimeOffset.UtcNow);
        var reclaimed = (await AcquireAsync(factory, runId, "trace-worker-b", DateTimeOffset.UtcNow))!;
        var second = first with { StepId = Guid.CreateVersion7(), StepKey = "trace.second", OperationId = Guid.CreateVersion7() };
        Assert.IsFalse(await UseStoreAsync(factory, async store => await store.CommitProgressAsync(new(lease, "running", second, null, null))));
        string secondTrace;
        using (var root = new Activity("trace.second.worker").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            Assert.IsTrue(await UseStoreAsync(factory, async store => await store.CommitProgressAsync(new(reclaimed, "completed", second, null, null))));
            secondTrace = root.TraceId.ToHexString();
        }
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        foreach (var (step, trace) in new[] { (first, firstTrace), (second, secondTrace) })
        {
            Assert.AreEqual(trace, await connection.QuerySingleAsync<string?>(
                "SELECT TraceId FROM fn_ai_agent_step WHERE Id=@Id AND RunId=@RunId", new { Id = step.StepId, RunId = runId }));
            var span = spans.Single(activity => Equals(activity.GetTagItem("ai.step.id"), step.StepId.ToString("D")));
            Assert.AreEqual(trace, span.TraceId.ToHexString()); Assert.AreEqual("completed", span.GetTagItem("ai.outcome"));
        }
        Assert.AreNotEqual(firstTrace, secondTrace); Assert.AreEqual(2, spans.Count);
        Assert.AreEqual(2L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_step WHERE RunId=@RunId", new { RunId = runId }));
    }

    private static async Task Concurrent_claim_admits_only_one_owner_async(FullNetApiFactory factory)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = await SeedQueuedRunAsync(factory, now).ConfigureAwait(false);
        var claims = await Task.WhenAll(
            AcquireAsync(factory, runId, "worker-a", now),
            AcquireAsync(factory, runId, "worker-b", now)).ConfigureAwait(false);
        Assert.AreEqual(1, claims.Count(lease => lease is not null));
        Assert.AreEqual(1, claims.Count(lease => lease is null));
        var owner = claims.Single(lease => lease is not null)!;
        Assert.AreEqual(runId, owner.RunId);
        Assert.IsGreaterThan(0L, owner.Epoch);
    }

    private static async Task Expired_lease_can_be_reclaimed_by_another_worker_async(FullNetApiFactory factory)
    {
        var seedTime = DateTimeOffset.UtcNow;
        var runId = await SeedQueuedRunAsync(factory, seedTime).ConfigureAwait(false);
        var first = await AcquireAsync(factory, runId, "worker-a", DateTimeOffset.UtcNow).ConfigureAwait(false);
        Assert.IsNotNull(first);
        await ExpireLeaseAsync(factory, runId, DateTimeOffset.UtcNow).ConfigureAwait(false);
        var reclaimed = await AcquireAsync(factory, runId, "worker-b", DateTimeOffset.UtcNow).ConfigureAwait(false);
        Assert.IsNotNull(reclaimed);
        Assert.AreEqual("worker-b", await LeaseOwnerAsync(factory, runId).ConfigureAwait(false));
    }

    private static async Task Stale_lease_cannot_commit_terminal_progress_async(FullNetApiFactory factory)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = await SeedQueuedRunAsync(factory, now).ConfigureAwait(false);
        var stale = await AcquireAsync(factory, runId, "worker-stale", now).ConfigureAwait(false);
        Assert.IsNotNull(stale);
        await ExpireLeaseAsync(factory, runId, now).ConfigureAwait(false);
        Assert.IsNotNull(await AcquireAsync(factory, runId, "worker-current", now.AddMinutes(2)).ConfigureAwait(false));
        var committed = await UseStoreAsync(factory, async store => await store.CommitProgressAsync(
            new AgentRunProgressCommit(stale!, "completed", null, null, null)).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.IsFalse(committed);
        Assert.AreEqual("running", await StatusAsync(factory, runId).ConfigureAwait(false));
    }

    private static async Task Renew_requires_current_version_async(FullNetApiFactory factory)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = await SeedQueuedRunAsync(factory, now).ConfigureAwait(false);
        var lease = await AcquireAsync(factory, runId, "worker-renew", now).ConfigureAwait(false);
        Assert.IsNotNull(lease);
        Assert.IsTrue(await UseStoreAsync(factory, async store =>
            await store.RenewAsync(lease!, now.AddSeconds(5), TimeSpan.FromSeconds(60)).ConfigureAwait(false)).ConfigureAwait(false));
        Assert.IsFalse(await UseStoreAsync(factory, async store =>
            await store.RenewAsync(lease!, now.AddSeconds(10), TimeSpan.FromSeconds(60)).ConfigureAwait(false)).ConfigureAwait(false));
    }

    private static async Task Live_renewals_handoff_versions_and_commit_async(FullNetApiFactory factory)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = await SeedQueuedRunAsync(factory, now);
        var lease = (await AcquireAsync(factory, runId, "worker-handoff", now))!;
        for (var i = 0; i < 2; i++)
        {
            var renewedAt = DateTimeOffset.UtcNow;
            Assert.IsTrue(await UseStoreAsync(factory, async store => await store.RenewAsync(lease, renewedAt, TimeSpan.FromSeconds(60))));
            lease = lease with { Version = lease.Version + 1, ExpiresAtUtc = renewedAt.AddSeconds(60) };
        }
        Assert.IsTrue(await UseStoreAsync(factory, async store => await store.CommitProgressAsync(new(lease, "completed", null, null, null))));
        Assert.AreEqual("completed", await StatusAsync(factory, runId));
    }

    private static async Task Expired_authority_cannot_renew_or_commit_async(FullNetApiFactory factory, bool deadline)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = await SeedQueuedRunAsync(factory, now);
        var lease = (await AcquireAsync(factory, runId, "worker-expired", now))!;
        // 仅改有效期，保留原版本与所有者；不能把依赖抢占后的 CAS 拒绝当成到期拒绝证据。
        await using (DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false)))
        {
            await connection.ExecuteAsync(deadline
                ? "UPDATE fn_ai_agent_run SET DeadlineAtUtc = @Expired WHERE Id = @RunId"
                : "UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc = @Expired WHERE Id = @RunId",
                new { RunId = runId, Expired = now.AddMinutes(-1) });
        }
        Assert.IsFalse(await UseStoreAsync(factory, async store => await store.RenewAsync(lease, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60))));
        Assert.IsFalse(await UseStoreAsync(factory, async store => await store.CommitProgressAsync(new(lease, "completed", null, null, null))));
        Assert.AreEqual("running", await StatusAsync(factory, runId));
    }

    private static async Task Terminal_run_cannot_be_rewritten_async(FullNetApiFactory factory)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = await SeedQueuedRunAsync(factory, now);
        var lease = (await AcquireAsync(factory, runId, "worker-terminal", now))!;
        Assert.IsTrue(await UseStoreAsync(factory, async store => await store.CommitProgressAsync(new(lease, "completed", null, null, null))));
        var terminalLease = lease with { Version = lease.Version + 1 };
        Assert.IsFalse(await UseStoreAsync(factory, async store => await store.CommitProgressAsync(new(terminalLease, "failed", null, null, null))));
        Assert.IsFalse(await UseStoreAsync(factory, async store => await store.RenewAsync(terminalLease, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60))));
        Assert.AreEqual("completed", await StatusAsync(factory, runId));
    }

    private static async Task<Guid> SeedQueuedRunAsync(FullNetApiFactory factory, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var runId = ids.NewId();
            var modelConfigId = ids.NewId();
            var draft = new AgentRunDraft(
                Guid.CreateVersion7(),
                new string('B', 64),
                "host",
                null,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.NewGuid().ToString("N"),
                "host-admin",
                "host",
                "fullnet-single-text-v1",
                1,
                $$"""{"modelConfigId":"{{modelConfigId}}","prompt":"lease-test","inputTokenLimit":100,"outputTokenLimit":100}""",
                now.AddHours(1),
                runId);
            return await scope.ServiceProvider.GetRequiredService<IAgentRunStore>().CreateOrGetAsync(draft).ConfigureAwait(false);
        }
        finally
        {
            tenant.Clear();
        }
    }

    private static async Task<AgentRunLease?> AcquireAsync(
        FullNetApiFactory factory, Guid runId, string workerId, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IAgentRunStore>()
                .TryAcquireAsync(runId, workerId, now, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }
        finally
        {
            tenant.Clear();
        }
    }

    private static async Task<T> UseStoreAsync<T>(FullNetApiFactory factory, Func<IAgentRunStore, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            return await action(scope.ServiceProvider.GetRequiredService<IAgentRunStore>()).ConfigureAwait(false);
        }
        finally
        {
            tenant.Clear();
        }
    }

    private static async Task ExpireLeaseAsync(FullNetApiFactory factory, Guid runId, DateTimeOffset now)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        await connection.ExecuteAsync(
            "UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc = @Expired WHERE Id = @RunId",
            new { RunId = runId, Expired = now.AddMinutes(-5) }).ConfigureAwait(false);
    }

    private static async Task<string> LeaseOwnerAsync(FullNetApiFactory factory, Guid runId)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        return await connection.QuerySingleAsync<string>(
            "SELECT LeaseOwner FROM fn_ai_agent_run WHERE Id = @RunId",
            new { RunId = runId }).ConfigureAwait(false);
    }

    private static async Task<string> StatusAsync(FullNetApiFactory factory, Guid runId)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        return await connection.QuerySingleAsync<string>(
            "SELECT StatusKey FROM fn_ai_agent_run WHERE Id = @RunId",
            new { RunId = runId }).ConfigureAwait(false);
    }
}
