using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.Logging;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AccountChallengeReconciliationWorkerTests
{
    [TestMethod]
    public void Worker_registers_delivery_reconciliation_without_http_challenge_delivery()
    {
        var services = new ServiceCollection();
        new IdentityModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
        Assert.AreEqual(1, services.Count(item => item.ServiceType == typeof(IHostedService)
            && item.ImplementationType?.Name == "AccountChallengeReconciliationHostedProcessor"));
    }

    [TestMethod]
    [DataRow(0, 60, false)]
    [DataRow(501, 60, false)]
    [DataRow(100, 29, false)]
    [DataRow(100, 3601, false)]
    [DataRow(1, 30, true)]
    [DataRow(500, 3600, true)]
    public void Reconciliation_configuration_has_bounded_pages_and_polling(int batch, int poll, bool valid)
    {
        var options = new AccountChallengeReconciliationOptions { BatchSize = batch, PollSeconds = poll };
        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(valid, new AccountChallengeReconciliationOptionsValidator().Validate(null, options).Succeeded);
    }

    [TestMethod]
    [DataRow("unknown", false, false, false)]
    [DataRow("unknown", true, false, true)]
    [DataRow("unknown", false, true, true)]
    [DataRow("rejected", true, false, true)]
    [DataRow("accepted", true, true, false)]
    [DataRow(null, false, true, false)]
    [DataRow("unrecognized", true, true, false)]
    public async Task Page_reconciles_only_completed_or_expired_unconfirmed_delivery(string? state, bool completed, bool expired, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var row = new AccountChallengeRecord(Guid.CreateVersion7(), 1, "user@example.test", "digest",
            now.AddMinutes(expired ? -1 : 15), null, 0, 5, 1, now, state, completed ? now : null);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<AccountChallengeRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { row });
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var page = await new AccountChallengeReconciliationRunner(query, command, clock,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer })).RunPageAsync(null, 10, CancellationToken.None);
        Assert.AreEqual(1, page.Scanned);
        Assert.AreEqual(expected ? 1 : 0, page.Reconciled);
        Assert.IsNull(page.NextAfterId);
        Assert.AreEqual(expected ? 1 : 0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Full_page_advances_database_cursor_even_if_another_worker_won_the_cas(DatabaseProvider provider)
    {
        var now = DateTimeOffset.UtcNow;
        var after = Guid.NewGuid(); var id = Guid.CreateVersion7();
        var query = Substitute.For<IQueryExecutor>();
        SqlStatement? selected = null;
        IReadOnlyDictionary<string, object?>? parameters = null;
        query.QueryAsync<AccountChallengeRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => { selected = call.ArgAt<SqlStatement>(0); parameters = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
                return new[] { new AccountChallengeRecord(id, 1, "user@example.test", "digest", now, null, 0, 5, 1, now, "unknown") }; });
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var result = await new AccountChallengeReconciliationRunner(query, command, clock,
            Options.Create(new DatabaseOptions { Provider = provider })).RunPageAsync(after, 1, CancellationToken.None);
        Assert.AreEqual(provider == DatabaseProvider.MySql ? AccountChallengeSql.ScanDeliveryMySql : AccountChallengeSql.ScanDeliverySqlServer, selected);
        Assert.AreEqual(after, parameters!["AfterId"]);
        Assert.AreEqual(1, parameters["BatchSize"]);
        Assert.AreEqual(id, result.NextAfterId);
        Assert.AreEqual(0, result.Reconciled);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(501)]
    public async Task Invalid_page_size_is_rejected_before_database_access(int size)
    {
        var query = Substitute.For<IQueryExecutor>();
        var runner = new AccountChallengeReconciliationRunner(query, Substitute.For<ICommandExecutor>(), Substitute.For<IClock>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.MySql }));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => runner.RunPageAsync(null, size, CancellationToken.None));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Worker_clears_host_context_after_a_failed_page_and_stops_on_cancellation()
    {
        var accessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<AccountChallengeRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { accessed.TrySetResult(); return Task.FromException<IReadOnlyList<AccountChallengeRecord>>(new IOException("sensitive-database-detail")); });
        var tenant = Substitute.For<ICurrentTenantContextWriter>();
        var monitor = Substitute.For<IOptionsMonitor<AccountChallengeReconciliationOptions>>();
        monitor.CurrentValue.Returns(new AccountChallengeReconciliationOptions { Enabled = true });
        var services = new ServiceCollection();
        services.AddSingleton(query); services.AddSingleton(Substitute.For<ICommandExecutor>());
        services.AddSingleton(Substitute.For<IClock>()); services.AddSingleton(tenant);
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        services.AddScoped<AccountChallengeReconciliationRunner>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var logger = Substitute.For<ILogger<AccountChallengeReconciliationHostedProcessor>>(); logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        using var worker = new AccountChallengeReconciliationHostedProcessor(provider.GetRequiredService<IServiceScopeFactory>(), monitor, logger);
        await worker.StartAsync(CancellationToken.None);
        await accessed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);
        tenant.Received(1).SetHost(); tenant.Received(1).Clear();
        Assert.AreEqual(1, query.ReceivedCalls().Count());
        foreach (var call in logger.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "Log"))
        {
            Assert.IsNull(call.GetArguments()[3], "巡检诊断不得携带原始数据库异常。");
            Assert.IsFalse(call.GetArguments()[2]!.ToString()!.Contains("sensitive-database-detail", StringComparison.Ordinal));
        }
    }
}
