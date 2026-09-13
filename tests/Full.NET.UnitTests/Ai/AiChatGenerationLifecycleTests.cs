using Full.NET.AI.Abstractions.Budgets;
using System.IO.Pipelines;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Extensions;

namespace Full.NET.UnitTests.Ai;

/// <summary>验证启动事务、HTTP 失败清理、租约过期显示及跨作用域取消检查。</summary>
[TestClass]
public sealed class AiChatGenerationLifecycleTests
{
    /// <summary>HTTP 启动失败释放已提交租约，消息写入失败则整个启动事务回滚。</summary>
    /// <param name="failInsertion">是否在消息写入阶段失败。</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Startup_failure_does_not_leave_generation_owned_async(bool failInsertion)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetHost();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
        var sessionId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.NewGuid(), generationId);
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<AiChatSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiChatSessionRecord { Id = sessionId, OwnerUserId = ownerId, ModelConfigId = Guid.NewGuid() });
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelConfigRecord { IsEnabled = true, ProviderKey = "ollama" });
        queries.ReturnsForAll<Task<IReadOnlyList<AiChatMessageRecord>>>(Task.FromResult<IReadOnlyList<AiChatMessageRecord>>([]));
        var coordinator = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(coordinator);
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (call.Arg<SqlStatement>() == AiChatSql.InsertMessage)
            {
                Assert.IsTrue(coordinator.HasTransaction);
                if (failInsertion) throw new IOException("message insert failed");
            }
            if (call.Arg<SqlStatement>() == AiChatGenerationSql.Release) Assert.IsFalse(coordinator.HasTransaction);
            return 1;
        });
        var budget = Substitute.For<IAiOperationBudgetStore>();
        budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            new AiOperationReservation(call.Arg<AiOperationRequest>()!.OperationId, true, "reserved", 100, null, null));
        await using var services = new ServiceCollection()
            .AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>()
            .AddScoped<ICommandExecutor>(_ => commands).AddScoped<IAiOperationBudgetStore>(_ => budget).BuildServiceProvider();
        var monitor = new AiChatGenerationLeaseMonitor(services.GetRequiredService<IServiceScopeFactory>(), clock,
            NullLogger<AiChatGenerationLeaseMonitor>.Instance);
        var registry = new AiChatGenerationRegistry();
        var options = Options.Create(new DatabaseOptions());
        var service = new AiChatStreamService(queries, commands, transaction,
            new AiChatSessionQueryService(queries, tenant, options, clock),
            TestAiProviders.Streamer(Substitute.For<IHttpClientFactory>()), registry, monitor,
            budget,
            new AiChatCleanupScope(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiChatCleanupScope>.Instance),
            tenant, options, clock, ids);
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        var response = Substitute.For<IHttpResponseBodyFeature>();
        response.Stream.Returns(body);
        response.Writer.Returns(PipeWriter.Create(body));
        response.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("HTTP start failed")));
        context.Features.Set(response);
        await service.StreamAsync(sessionId, ownerId, new StreamAiChatMessageRequest("hello"), new AiChatHttpOutput(context));
        await response.Received(failInsertion ? 0 : 1).StartAsync(Arg.Any<CancellationToken>());
        Assert.AreEqual(failInsertion ? 1 : 0, coordinator.RollbackCount);
        Assert.AreEqual(failInsertion ? 0 : 1, coordinator.CommitCount);
        await commands.Received(failInsertion ? 0 : 1).ExecuteAsync(AiChatGenerationSql.Release,
            Arg.Is<object?>(value => ((IReadOnlyDictionary<string, object?>)value!)["GenerationId"]!.Equals(generationId)), Arg.Any<CancellationToken>());
        Assert.IsFalse(registry.TryCancel(sessionId, generationId));
    }

    /// <summary>租约到期后详情不再告诉客户端永久等待旧生成。</summary>
    [TestMethod]
    public void Expired_generation_is_not_reported_as_running()
    {
        var now = DateTimeOffset.UtcNow;
        var response = AiChatMapper.MapDetail(new AiChatSessionRecord
            { IsGenerating = true, GenerationExpiresAtUtc = now.AddSeconds(-1) }, [], now);
        Assert.IsFalse(response.IsGenerating);
    }

    /// <summary>远程取消或失去租约返回零时，监视器不能报告续租成功。</summary>
    [TestMethod]
    public async Task Remote_cancellation_denies_renewal_in_independent_scope_async()
    {
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>();
        services.AddScoped<ICommandExecutor>(_ => commands);
        await using var provider = services.BuildServiceProvider();
        var monitor = new AiChatGenerationLeaseMonitor(provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IClock>(), NullLogger<AiChatGenerationLeaseMonitor>.Instance);
        Assert.IsFalse(await monitor.RenewOnceAsync(new AiChatScope(null), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default));
        await commands.Received(1).ExecuteAsync(AiChatGenerationSql.Renew, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
    /// <summary>数据库忽略取消并一直等待时，本地租约期限仍中止提供程序令牌。</summary>
    [TestMethod]
    public async Task Stalled_renewal_cancels_provider_at_local_lease_deadline_async()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>();
        services.AddScoped<ICommandExecutor>(_ => commands);
        await using var provider = services.BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var monitor = new AiChatGenerationLeaseMonitor(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            NullLogger<AiChatGenerationLeaseMonitor>.Instance);
        using var request = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = request.Token.Register(() => cancelled.TrySetResult(true));
        var watching = monitor.WatchAsync(new AiChatScope(null), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            now.AddMilliseconds(150), request, stop.Token);
        try
        {
            Assert.IsTrue(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.IsFalse(pending.Task.IsCompleted, "模拟驱动仍未返回，本地期限不能依赖其配合。");
        }
        finally
        {
            pending.TrySetResult(0);
            await stop.CancelAsync();
            await watching;
        }
    }
}
