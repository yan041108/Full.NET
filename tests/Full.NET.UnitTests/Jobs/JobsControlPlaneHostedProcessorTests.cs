using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Jobs;

/// <summary>控制面独立循环；计划查询等待时心跳仍能提交，关闭时收敛等待。</summary>
[TestClass]
public sealed class JobsControlPlaneHostedProcessorTests
{
    [TestMethod]
    public async Task Waiting_schedule_does_not_block_heartbeat()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heartbeatWritten=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = new WaitingScheduleQuery(entered);
        var clock=new SystemClock(); var database=Options.Create(new DatabaseOptions { Provider=DatabaseProvider.SqlServer });
        var options=Options.Create(new JobsWorkerOptions { PollMilliseconds=250 });
        var services=new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenantContextWriter>(provider => provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICommandExecutor>(provider => {
            var context=provider.GetRequiredService<CurrentTenantAccessor>(); var commands=Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(),Arg.Any<object>(),Arg.Any<CancellationToken>()).Returns(call => {
                Assert.IsTrue(context.IsHost);
                if (call.Arg<SqlStatement>() == JobSql.UpsertWorkerHeartbeat) heartbeatWritten.TrySetResult();
                return 1;
            }); return commands;
        });
        services.AddScoped(provider => new JobScheduleDispatcher(queries,provider.GetRequiredService<ICommandExecutor>(),
            new Transaction(),database,clock,Substitute.For<IIdGenerator>()));
        await using var provider=services.BuildServiceProvider(); var scopes=provider.GetRequiredService<IServiceScopeFactory>();
        using var processor=new JobsControlPlaneHostedProcessor(scopes,new JobWorkerHeartbeatService(scopes,clock,database,options),
            options,NullLogger<JobsControlPlaneHostedProcessor>.Instance);
        await processor.StartAsync(CancellationToken.None);
        try { await Task.WhenAll(entered.Task,heartbeatWritten.Task).WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)); await processor.StopAsync(deadline.Token); }
    }
    private sealed class WaitingScheduleQuery(TaskCompletionSource entered) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) => Task.FromResult(default(T));

        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(typeof(JobScheduleRecord), typeof(T));
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Array.Empty<T>();
        }
    }

    private sealed class Transaction : ICommandTransaction {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken cancellationToken) => action(cancellationToken);
    }
}
