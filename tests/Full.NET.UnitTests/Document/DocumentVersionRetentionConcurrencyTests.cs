using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.UpdateHostDocumentVersionRetention;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Globalization;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Resources;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentVersionRetentionConcurrencyTests
{
    [TestMethod]
    [DataRow("zh-CN", "document.version_retention.conflict", "文档版本保留设置已被更新，请刷新后重试。")]
    [DataRow("en-US", "document.version_retention.conflict", "The document version retention settings have changed. Refresh before saving again.")]
    [DataRow("zh-CN", "document.version_retention.invalid", "文档版本保留设置或修订号无效。")]
    [DataRow("en-US", "document.version_retention.invalid", "The document version retention settings or revision are invalid.")]
    public void Retention_errors_localize_with_stable_machine_codes(string culture, string code, string expected)
    {
        var localizer = new ResourceErrorMessageLocalizer([new DocumentErrorResourceSource()], new NamedMessageFormatter());
        var message = localizer.Localize(new Full.NET.Abstractions.Results.Error(code, "fallback", Full.NET.Abstractions.Results.ErrorType.Conflict), CultureInfo.GetCultureInfo(culture));
        Assert.AreEqual(expected, message);
    }

    [TestMethod]
    public void Settings_contracts_expose_revision_without_changing_four_argument_construction()
    {
        var request = new UpdateHostDocumentVersionRetentionRequest(1, 0, 300, 50);
        var response = new HostDocumentVersionRetentionSettingsResponse(1, 0, 300, 50);
        Assert.IsNotNull(request.GetType().GetProperty("Version"));
        Assert.IsNotNull(response.GetType().GetProperty("Version"));
        var (minimum, maximum, poll, batch) = request;
        Assert.AreEqual((1, 0, 300, 50), (minimum, maximum, poll, batch));
    }

    [TestMethod]
    public async Task Unversioned_request_cannot_overwrite_existing_database_policy()
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentVersionRetentionSettingRecord { Version = 7, BatchSize = 25 });
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var repository = new DocumentVersionRetentionSettingRepository(queries, commands,
            Substitute.For<IClock>(), Options.Create(new DatabaseOptions { Provider = DatabaseProvider.MySql }));
        var tenant = new CurrentTenantAccessor();
        tenant.SetHost();
        using var provider = new ServiceCollection()
            .AddSingleton(repository).AddSingleton<ICurrentTenantContextWriter>(tenant).BuildServiceProvider();
        using var tokens = new DocumentVersionRetentionOptionsChangeTokenSource();
        var bootstrap = new DocumentVersionRetentionSettingsBootstrap(provider.GetRequiredService<IServiceScopeFactory>(),
            new DocumentVersionRetentionSettingsStore(), tokens, NullLogger<DocumentVersionRetentionSettingsBootstrap>.Instance);
        var monitor = Substitute.For<IOptionsMonitor<DocumentVersionRetentionOptions>>();
        monitor.CurrentValue.Returns(new DocumentVersionRetentionOptions());
        var service = new DocumentVersionRetentionSettingService(repository, bootstrap, monitor);

        var result = await service.UpdateAsync(new UpdateHostDocumentVersionRetentionRequest(1, 0, 300, 50), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("document.version_retention.conflict", result.Error!.Code);
        await commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public void Older_reload_cannot_replace_newer_effective_policy()
    {
        var store = new DocumentVersionRetentionSettingsStore();
        store.SetHostOverride(new DocumentVersionRetentionSettingRecord { Version = 8, BatchSize = 80 });
        store.SetHostOverride(new DocumentVersionRetentionSettingRecord { Version = 7, BatchSize = 70 });
        var options = new DocumentVersionRetentionOptions();
        store.ApplyOverrides(options);
        Assert.AreEqual(80, options.BatchSize);
    }

    [TestMethod]
    public void Missing_database_override_restores_configuration_values()
    {
        var store = new DocumentVersionRetentionSettingsStore();
        store.SetHostOverride(new DocumentVersionRetentionSettingRecord { Version = 8, BatchSize = 80 });
        store.SetHostOverride(null);
        var options = new DocumentVersionRetentionOptions { BatchSize = 50 };
        store.ApplyOverrides(options);
        Assert.AreEqual(50, options.BatchSize);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Stale_version_does_not_reload_or_report_success(DatabaseProvider provider)
    {
        using var fixture = new Fixture(provider);
        fixture.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await fixture.Service.UpdateAsync(Request(7), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("document.version_retention.conflict", result.Error!.Code);
        await fixture.Queries.DidNotReceiveWithAnyArgs()
            .QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(default!, default, default);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Successful_update_returns_its_commit_even_if_reload_observes_a_later_write(DatabaseProvider provider)
    {
        using var fixture = new Fixture(provider);
        fixture.Queries.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentVersionRetentionSettingRecord { Version = 9, BatchSize = 99 });
        var request = Request(7);
        var result = await fixture.Service.UpdateAsync(request, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(8L, result.Value!.Version);
        Assert.AreEqual(request.BatchSize, result.Value.BatchSize);
        await fixture.Commands.Received(1).ExecuteAsync(
            Arg.Is<SqlStatement>(sql => sql != null && sql.Scope == SqlDataScope.HostOnly
                && sql.Text.Contains("TenantId IS NULL AND Version = @Version", StringComparison.Ordinal)),
            Arg.Is<object?>(parameters => parameters is Dictionary<string, object?>
                && (long)((Dictionary<string, object?>)parameters)["Version"]! == 7),
            CancellationToken.None);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task First_write_initializes_revision_one(DatabaseProvider provider)
    {
        using var fixture = new Fixture(provider);
        var result = await fixture.Service.UpdateAsync(Request(0), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1L, result.Value!.Version);
        await fixture.Commands.Received(1).ExecuteAsync(
            Arg.Is<SqlStatement>(sql => sql != null && sql.Text.StartsWith("INSERT INTO", StringComparison.Ordinal)),
            Arg.Any<object?>(), CancellationToken.None);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Concurrent_initial_insert_unique_failure_becomes_conflict(DatabaseProvider provider)
    {
        using var fixture = new Fixture(provider);
        fixture.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DataCommandException(DataCommandFailureKind.UniqueConstraint, new InvalidOperationException())));
        var result = await fixture.Service.UpdateAsync(Request(0), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("document.version_retention.conflict", result.Error!.Code);
    }

    [TestMethod]
    public async Task Unclassified_database_failure_is_not_disguised_as_conflict()
    {
        using var fixture = new Fixture(DatabaseProvider.MySql);
        var failure = new InvalidOperationException("database unavailable");
        fixture.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(failure));
        var caught = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.UpdateAsync(Request(0), CancellationToken.None));
        Assert.AreSame(failure, caught);
    }

    [TestMethod]
    public async Task Cancellation_propagates_without_a_successful_policy_reload()
    {
        using var fixture = new Fixture(DatabaseProvider.MySql);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), cancellation.Token)
            .Returns(Task.FromCanceled<int>(cancellation.Token));
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => fixture.Service.UpdateAsync(Request(7), cancellation.Token));
        await fixture.Queries.DidNotReceiveWithAnyArgs()
            .QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(default!, default, default);
    }

    [TestMethod]
    [DataRow(-1L)]
    [DataRow(long.MaxValue)]
    public async Task Invalid_version_never_reaches_database(long version)
    {
        using var fixture = new Fixture(DatabaseProvider.MySql);
        var result = await fixture.Service.UpdateAsync(Request(version), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("document.version_retention.invalid", result.Error!.Code);
        await fixture.Commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        await fixture.Queries.DidNotReceiveWithAnyArgs()
            .QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(default!, default, default);
    }

    [TestMethod]
    public async Task Read_pairs_database_values_and_revision_instead_of_cached_options()
    {
        using var fixture = new Fixture(DatabaseProvider.MySql);
        fixture.Queries.QuerySingleOrDefaultAsync<DocumentVersionRetentionSettingRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentVersionRetentionSettingRecord { Version = 7, BatchSize = 75 });
        var result = await fixture.Service.GetEffectiveAsync(CancellationToken.None);
        Assert.AreEqual(7L, result.Version);
        Assert.AreEqual(75, result.BatchSize);
    }

    [TestMethod]
    public async Task Read_without_override_returns_configuration_with_revision_zero()
    {
        using var fixture = new Fixture(DatabaseProvider.MySql);
        var result = await fixture.Service.GetEffectiveAsync(CancellationToken.None);
        Assert.AreEqual(0L, result.Version);
        Assert.AreEqual(50, result.BatchSize);
    }

    private static UpdateHostDocumentVersionRetentionRequest Request(long version) => new(1, 0, 300, 50) { Version = version };

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly DocumentVersionRetentionOptionsChangeTokenSource _tokens = new();
        public IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public DocumentVersionRetentionSettingService Service { get; }

        public Fixture(DatabaseProvider provider)
        {
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            var repository = new DocumentVersionRetentionSettingRepository(Queries, Commands,
                Substitute.For<IClock>(), Options.Create(new DatabaseOptions { Provider = provider }));
            var tenant = new CurrentTenantAccessor();
            tenant.SetHost();
            _provider = new ServiceCollection().AddSingleton(repository)
                .AddSingleton<ICurrentTenantContextWriter>(tenant).BuildServiceProvider();
            var bootstrap = new DocumentVersionRetentionSettingsBootstrap(_provider.GetRequiredService<IServiceScopeFactory>(),
                new DocumentVersionRetentionSettingsStore(), _tokens, NullLogger<DocumentVersionRetentionSettingsBootstrap>.Instance);
            var monitor = Substitute.For<IOptionsMonitor<DocumentVersionRetentionOptions>>();
            monitor.CurrentValue.Returns(new DocumentVersionRetentionOptions());
            Service = new(repository, bootstrap, monitor);
        }

        public void Dispose()
        {
            _tokens.Dispose();
            _provider.Dispose();
        }
    }
}
