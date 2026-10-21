using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>消费排空不应重复扫描已经占用的来源；扫描故障仍须退避。</summary>
[TestClass]
public sealed class WorkflowRecoveryScanBudgetTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Consumer_iterations_reuse_scan_interval_and_failures_are_throttled(bool failScan)
    {
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(_ => now);
        var queries = Substitute.For<IQueryExecutor>();
        var scans = 0;
        queries.QueryAsync<WorkflowRecoveryScanCandidate>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { scans++; if (failScan) throw new InvalidOperationException("scan"); return Task.FromResult<IReadOnlyList<WorkflowRecoveryScanCandidate>>([new(null, "host", "host", Guid.CreateVersion7(), null)]); });
        queries.QueryAsync<WorkflowRecoveryTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryTaskRecord>>([]));
        // 每类都未满，三类合计超过批大小也不能误判仍有未发现积压。
        var options = Options.Create(new WorkflowRecoveryWorkerOptions { ScanIntervalSeconds = 15, BatchSize = 2 });
        var services = new ServiceCollection();
        services.AddSingleton(queries);
        services.AddSingleton(Substitute.For<ICommandExecutor>());
        services.AddSingleton(Substitute.For<ICommandTransaction>());
        services.AddSingleton(clock);
        services.AddSingleton(Substitute.For<IIdGenerator>());
        services.AddSingleton(options);
        services.AddSingleton(Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<WorkflowRecoveryBatchProcessor>>(NullLogger<WorkflowRecoveryBatchProcessor>.Instance);
        services.AddScoped<WorkflowRecoveryScanner>(); services.AddScoped<WorkflowRecoveryBatchProcessor>();
        await using var provider = services.BuildServiceProvider();
        var hosted = new WorkflowRecoveryHostedProcessor(provider.GetRequiredService<IServiceScopeFactory>(), clock, options,
            NullLogger<WorkflowRecoveryHostedProcessor>.Instance);
        if (failScan) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => hosted.ProcessOnceAsync(default));
        else await hosted.ProcessOnceAsync(default);
        var first = scans;
        for (var i = 0; i < 10; i++) await hosted.ProcessOnceAsync(default);
        Assert.AreEqual(first, scans);
        now = now.AddSeconds(15);
        if (failScan) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => hosted.ProcessOnceAsync(default));
        else await hosted.ProcessOnceAsync(default);
        Assert.AreEqual(first * 2, scans);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Scans_pass_bounded_kind_parameters_and_do_not_insert_without_candidates(DatabaseProvider provider)
    {
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        queries.QueryAsync<WorkflowRecoveryScanCandidate>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryScanCandidate>>([]));
        var scanner = new WorkflowRecoveryScanner(queries, commands, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = provider }), Options.Create(new WorkflowRecoveryWorkerOptions { BatchSize = 7 }));
        Assert.AreEqual(0, await scanner.ScanAsync(default));
        Assert.AreEqual(3, queries.ReceivedCalls().Count());
        foreach (var call in queries.ReceivedCalls())
        {
            var args = call.GetArguments();
            var parameters = (IReadOnlyDictionary<string, object?>)args[1]!;
            Assert.AreEqual(7, parameters["BatchSize"]);
            Assert.IsFalse(string.IsNullOrEmpty((string?)parameters["KindKey"]));
        }
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
}
