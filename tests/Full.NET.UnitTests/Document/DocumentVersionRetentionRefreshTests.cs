using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Retention;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentVersionRetentionRefreshTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Change_callback_observes_fresh_token_even_when_listener_throws(bool throwFromCallback)
    {
        using var source = new DocumentVersionRetentionOptionsChangeTokenSource();
        IChangeToken? next = null;
        using var registration = source.GetChangeToken().RegisterChangeCallback(_ =>
        {
            next = source.GetChangeToken();
            if (throwFromCallback) throw new InvalidOperationException("listener failure");
        }, null);

        if (throwFromCallback) Assert.ThrowsExactly<AggregateException>(source.SignalChange);
        else source.SignalChange();

        Assert.IsNotNull(next);
        Assert.IsFalse(next.HasChanged, "回调重新订阅时必须取得尚未取消的新令牌。");
        Assert.IsFalse(source.GetChangeToken().HasChanged);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Module_monitor_receives_concrete_source_changes(bool worker)
    {
        using var fixture = new Fixture(worker);
        var monitor = fixture.Provider.GetRequiredService<IOptionsMonitor<DocumentVersionRetentionOptions>>();
        Assert.AreEqual(50, monitor.CurrentValue.BatchSize);
        fixture.Provider.GetRequiredService<DocumentVersionRetentionSettingsStore>().SetHostOverride(Record(2, 75));

        fixture.Provider.GetRequiredService<DocumentVersionRetentionOptionsChangeTokenSource>().SignalChange();

        Assert.AreEqual(75, monitor.CurrentValue.BatchSize);
    }

    [TestMethod]
    public void Repeated_api_and_worker_registration_produces_one_notification_per_revision()
    {
        using var fixture = new Fixture(true, registerBoth: true);
        var monitor = fixture.Provider.GetRequiredService<IOptionsMonitor<DocumentVersionRetentionOptions>>();
        var count = 0;
        using var subscription = monitor.OnChange((_, _) => count++);
        fixture.Provider.GetRequiredService<DocumentVersionRetentionSettingsStore>().SetHostOverride(Record(2, 80));

        fixture.Provider.GetRequiredService<DocumentVersionRetentionOptionsChangeTokenSource>().SignalChange();

        Assert.AreEqual(1, count);
        Assert.AreEqual(80, monitor.CurrentValue.BatchSize);
    }

    [TestMethod]
    public async Task Disabled_worker_refreshes_database_policy_before_deciding_to_skip_cleanup()
    {
        using var fixture = new Fixture(true);
        var monitor = fixture.Provider.GetRequiredService<IOptionsMonitor<DocumentVersionRetentionOptions>>();
        Assert.AreEqual(0, monitor.CurrentValue.MaximumRetainedHistoryVersions);
        fixture.Queries.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Record(3, 90));
        var processor = ActivatorUtilities.CreateInstance<DocumentVersionRetentionHostedProcessor>(fixture.Provider);

        var result = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(DocumentVersionRetentionResult.Empty, result);
        Assert.AreEqual(90, monitor.CurrentValue.BatchSize);
        await fixture.Queries.Received(1).QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Worker_database_refresh_failure_prevents_cleanup_with_stale_policy()
    {
        using var fixture = new Fixture(true);
        fixture.Queries.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DocumentVersionRetentionSettingRecord?>(new InvalidOperationException("database unavailable")));
        var processor = ActivatorUtilities.CreateInstance<DocumentVersionRetentionHostedProcessor>(fixture.Provider);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => processor.ProcessOnceAsync(CancellationToken.None));

        await fixture.Commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Worker_refresh_preserves_requested_cancellation()
    {
        using var fixture = new Fixture(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Queries.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<DocumentVersionRetentionSettingRecord?>(cancellation.Token));
        var processor = ActivatorUtilities.CreateInstance<DocumentVersionRetentionHostedProcessor>(fixture.Provider);

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => processor.ProcessOnceAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task Startup_loader_does_not_swallow_host_cancellation()
    {
        using var fixture = new Fixture(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Queries.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<DocumentVersionRetentionSettingRecord?>(cancellation.Token));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => fixture.Provider
            .GetRequiredService<DocumentVersionRetentionSettingsBootstrap>().StartAsync(cancellation.Token));
    }

    private static DocumentVersionRetentionSettingRecord Record(long version, int batchSize) => new()
    {
        Version = version, MinimumRetainedVersionsPerItem = 1, MaximumRetainedHistoryVersions = 0,
        PollSeconds = 60, BatchSize = batchSize
    };

    private sealed class Fixture : IDisposable
    {
        public IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public ServiceProvider Provider { get; }

        public Fixture(bool worker, bool registerBoth = false)
        {
            var tenant = new CurrentTenantAccessor();
            tenant.SetHost();
            var services = new ServiceCollection().AddLogging()
                .AddSingleton(Queries).AddSingleton(Commands)
                .AddSingleton<IClock>(Substitute.For<IClock>())
                .AddSingleton<ICurrentTenantContextWriter>(tenant)
                .AddSingleton(Options.Create(new DatabaseOptions { Provider = DatabaseProvider.MySql }));
            var configuration = new ConfigurationBuilder().Build();
            var module = new DocumentModule();
            if (worker) module.AddBackgroundServices(services, configuration);
            else module.AddServices(services, configuration);
            if (registerBoth)
            {
                module.AddServices(services, configuration);
                module.AddBackgroundServices(services, configuration);
            }
            Provider = services.BuildServiceProvider();
        }

        public void Dispose() => Provider.Dispose();
    }
}
