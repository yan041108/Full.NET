using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Data;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证两个 Jobs 实例在单实例普通准入争用时仍能完成原执行。</summary>
[TestClass]
[DoNotParallelize]
public sealed class JobsWorkerAdmissionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Saturated_worker_records_original_completion_and_another_worker_drains_jobs(DatabaseProvider provider) =>
        VerifyAdmissionAsync(provider, verifyRenewal: false);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Saturated_worker_renews_the_active_lease_before_recording_completion(DatabaseProvider provider) =>
        VerifyAdmissionAsync(provider, verifyRenewal: true);

    private static async Task VerifyAdmissionAsync(DatabaseProvider provider, bool verifyRenewal)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = budget.Token;
        var database = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var connectionString = WorkerAdmissionTestConfiguration.WithSmallPool(provider, database);
        var settings = WorkerAdmissionTestConfiguration.CreateSettings();
        var firstProbe = new JobProbe(block: true);
        var secondProbe = new JobProbe(block: false);
        using var first = new FullNetApiFactory(provider, connectionString, settings,
            configureTestServices: services =>
            {
                services.AddSingleton<IJobHandlerExecutor>(firstProbe);
                if (verifyRenewal)
                    services.PostConfigure<JobsWorkerOptions>(options =>
                    {
                        options.LeaseSeconds = 30;
                        options.LeaseRenewalSeconds = 5;
                    });
            });
        await first.InitializeAsync(token, useSchemaTemplate: false);
        using var second = new FullNetApiFactory(provider, connectionString, settings,
            configureTestServices: services => services.AddSingleton<IJobHandlerExecutor>(secondProbe));
        var original = await SeedAsync(first, token);

        var pollTimedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Name == "fullnet.db.connection.acquire")
                    active.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "outcome" && tag.Value is "timeout") pollTimedOut.TrySetResult();
        });
        listener.Start();

        var running = ProcessAsync(first, token);
        var permitHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePermit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? holding = null;
        Guid secondJob = default;
        try
        {
            Assert.AreEqual(original, await firstProbe.Started.Task.WaitAsync(token));
            holding = HoldNormalPermitAsync();
            await permitHeld.Task.WaitAsync(token);
            var originalLease = verifyRenewal ? await ReadLeaseAsync(second, original, token) : null;
            // 让取消轮询实际遇到一次普通准入超时，再允许 Handler 完成。
            await pollTimedOut.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            secondJob = await SeedAsync(second, token);
            Assert.AreEqual(1, await ProcessAsync(second, token));
            if (verifyRenewal)
                await WaitForRenewalAsync(second, original, originalLease, running, token);
            firstProbe.Release.TrySetResult();
            Assert.AreEqual(1, await running.WaitAsync(TimeSpan.FromSeconds(10), token));
            await AssertCompletedAsync(second, original, token);
            await AssertCompletedAsync(second, secondJob, token);
        }
        finally
        {
            releasePermit.TrySetResult();
            firstProbe.Release.TrySetResult();
            if (holding is not null) await holding;
            if (!running.IsCompleted) await running;
        }

        Assert.AreEqual(0, await ProcessAsync(first, token));
        Assert.AreEqual(0, await ProcessAsync(second, token));
        CollectionAssert.AreEqual(new[] { original }, firstProbe.Executions.ToArray());
        CollectionAssert.AreEqual(new[] { secondJob }, secondProbe.Executions.ToArray());

        async Task HoldNormalPermitAsync()
        {
            await using var scope = first.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellationToken =>
            {
                permitHeld.TrySetResult();
                await releasePermit.Task.WaitAsync(cancellationToken);
                return true;
            }, token);
        }
    }

    private static async Task WaitForRenewalAsync(
        FullNetApiFactory factory, Guid id, DateTimeOffset? initialLease, Task running, CancellationToken token)
    {
        Assert.IsNotNull(initialLease);
        using var renewalBudget = CancellationTokenSource.CreateLinkedTokenSource(token);
        renewalBudget.CancelAfter(TimeSpan.FromSeconds(12));
        var renewalToken = renewalBudget.Token;
        while (true)
        {
            if (running.IsCompleted)
            {
                await running;
                Assert.Fail("Handler 仍受控阻塞时，Worker 不应结束原执行。");
            }
            if (await ReadLeaseAsync(factory, id, renewalToken) > initialLease) return;
            await Task.Delay(100, renewalToken);
        }
    }

    private static async Task<DateTimeOffset?> ReadLeaseAsync(FullNetApiFactory factory, Guid id, CancellationToken token)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<DateTimeOffset?>(
            new SqlStatement("test.jobs.admission.read_lease", """
                SELECT LeaseExpiresAtUtc FROM fn_jobs_execution
                WHERE Id = @Id AND TenantId IS NULL AND Status = @Status
                """, SqlDataScope.HostOnly),
            new { Id = id, Status = JobExecutionStatuses.Running }, token);
    }

    private static async Task<Guid> SeedAsync(FullNetApiFactory factory, CancellationToken token)
    {
        var executionId = Guid.CreateVersion7();
        await JobsExecutionCancelAssertions.SeedDefinitionAndPendingExecutionAsync(
            factory, Guid.CreateVersion7(), executionId, token, JobProbe.Kind);
        return executionId;
    }

    private static async Task<int> ProcessAsync(FullNetApiFactory factory, CancellationToken token)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<JobExecutionRunner>().ProcessPendingAsync(1, token);
    }

    private static async Task AssertCompletedAsync(FullNetApiFactory factory, Guid id, CancellationToken token)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var completed = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(
            new SqlStatement("test.jobs.admission.completed", """
                SELECT COUNT(1) FROM fn_jobs_execution
                WHERE Id = @Id AND TenantId IS NULL AND Status = @Status AND AttemptCount = 1
                  AND LeaseId IS NULL AND LeaseExpiresAtUtc IS NULL AND FinishedAtUtc IS NOT NULL
                """, SqlDataScope.HostOnly),
            new { Id = id, Status = JobExecutionStatuses.Succeeded }, token);
        Assert.AreEqual(1L, completed, "普通许可仍被占用时，应能读回原执行及清理后的租约。");
    }

    private sealed class JobProbe(bool block) : IJobHandlerExecutor
    {
        internal const string Kind = "jobs.test-admission";
        internal TaskCompletionSource<Guid> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<Guid> Executions { get; } = new();
        public string HandlerKind => Kind;

        public async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
        {
            Executions.Enqueue(context.ExecutionId);
            Started.TrySetResult(context.ExecutionId);
            if (block) await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
