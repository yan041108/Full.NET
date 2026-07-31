using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class PendingHostFileReconciliationTests
{
    [TestMethod]
    public void Background_registration_validates_reconciliation_and_local_provider_options()
    {
        using var defaults = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Files:Local:RootPath"] = "worker-files",
            });
        var options = defaults.GetRequiredService<
            IOptions<PendingHostFileReconciliationOptions>>().Value;

        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(100, options.BatchSize);
        Assert.AreEqual(10, options.MaxBatchesPerRun);
        Assert.AreEqual(300, options.MinimumAgeSeconds);
        Assert.AreEqual(300, options.PollSeconds);
        Assert.IsTrue(defaults.GetServices<IHostedService>()
            .Any(service => service is PendingHostFileReconciliationHostedProcessor));
        defaults.GetRequiredService<IStartupValidator>().Validate();

        using var invalid = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Files:Local:RootPath"] = "worker-files",
                ["Files:UploadReconciliation:BatchSize"] = "0",
                ["Files:UploadReconciliation:MaxBatchesPerRun"] = "101",
                ["Files:UploadReconciliation:MinimumAgeSeconds"] = "29",
                ["Files:UploadReconciliation:PollSeconds"] = "4",
            });
        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);

        Assert.HasCount(4, exception.Failures);
    }

    [TestMethod]
    public async Task Disabled_reconciliation_does_not_access_database_or_storage()
    {
        var queryExecutor = new PendingQueryExecutor();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var provider = new ProbeStorage("local", exists: true);
        var runner = CreateRunner(queryExecutor, commandExecutor, provider);

        var result = await runner.RunOnceAsync(
            new PendingHostFileReconciliationOptions { Enabled = false },
            CancellationToken.None);

        Assert.AreEqual(PendingHostFileReconciliationResult.Empty, result);
        Assert.AreEqual(0, queryExecutor.QueryCount);
        Assert.AreEqual(0, provider.ProbeCount);
    }

    [TestMethod]
    public async Task Existing_blob_is_promoted_and_missing_blob_is_purged()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var existing = new PendingHostFileRecord(
            Guid.CreateVersion7(), "local", "host/existing", now.AddMinutes(-10));
        var missing = new PendingHostFileRecord(
            Guid.CreateVersion7(), "archive", "host/missing", now.AddMinutes(-9));
        var queryExecutor = new PendingQueryExecutor(
            new[] { existing, missing },
            Array.Empty<PendingHostFileRecord>());
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var runner = CreateRunner(
            queryExecutor,
            commandExecutor,
            new ProbeStorage("local", exists: true),
            new ProbeStorage("archive", exists: false),
            now);

        var result = await runner.RunOnceAsync(EnabledOptions(), CancellationToken.None);

        Assert.AreEqual(2, result.Scanned);
        Assert.AreEqual(1, result.Promoted);
        Assert.AreEqual(1, result.Purged);
        Assert.AreEqual(0, result.ProbeFailures);
        await commandExecutor.Received(1).ExecuteAsync(
            HostFileSql.ReconcileReady,
            Arg.Is<object>(value => HasFileId(value, existing.Id)),
            Arg.Any<CancellationToken>());
        await commandExecutor.Received(1).ExecuteAsync(
            HostFileSql.PurgePending,
            Arg.Is<object>(value => HasFileId(value, missing.Id)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Probe_failure_keeps_pending_record_for_retry()
    {
        var record = new PendingHostFileRecord(
            Guid.CreateVersion7(), "archive", "host/retry", DateTimeOffset.UtcNow.AddHours(-1));
        var queryExecutor = new PendingQueryExecutor(new[] { record });
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var runner = CreateRunner(
            queryExecutor,
            commandExecutor,
            new ProbeStorage("archive", new IOException("Provider unavailable.")));

        var result = await runner.RunOnceAsync(EnabledOptions(), CancellationToken.None);

        Assert.AreEqual(1, result.Scanned);
        Assert.AreEqual(1, result.ProbeFailures);
        Assert.AreEqual(0, result.Promoted);
        Assert.AreEqual(0, result.Purged);
        await commandExecutor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Missing_blob_with_active_publication_claim_is_retained()
    {
        var record = new PendingHostFileRecord(
            Guid.CreateVersion7(),
            "local",
            "host/slow-publication",
            DateTimeOffset.UtcNow.AddHours(-1),
            "publishing");
        var queryExecutor = new PendingQueryExecutor(new[] { record });
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var runner = CreateRunner(
            queryExecutor,
            commandExecutor,
            new ProbeStorage("local", exists: false));

        var result = await runner.RunOnceAsync(EnabledOptions(), CancellationToken.None);

        Assert.AreEqual(1, result.Scanned);
        Assert.AreEqual(1, result.RetainedPublishing);
        Assert.AreEqual(0, result.Purged);
        await commandExecutor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Reconciliation_fails_closed_when_transition_affects_multiple_rows()
    {
        var record = new PendingHostFileRecord(
            Guid.CreateVersion7(), "local", "host/corrupt", DateTimeOffset.UtcNow.AddHours(-1));
        var queryExecutor = new PendingQueryExecutor(new[] { record });
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                HostFileSql.ReconcileReady,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(2);
        var runner = CreateRunner(
            queryExecutor,
            commandExecutor,
            new ProbeStorage("local", exists: true));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunOnceAsync(EnabledOptions(), CancellationToken.None));
    }

    private static PendingHostFileReconciliationRunner CreateRunner(
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor,
        ProbeStorage provider,
        DateTimeOffset? now = null) =>
        CreateRunner(queryExecutor, commandExecutor, [provider], now);

    private static PendingHostFileReconciliationRunner CreateRunner(
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor,
        ProbeStorage first,
        ProbeStorage second,
        DateTimeOffset? now = null) =>
        CreateRunner(queryExecutor, commandExecutor, [first, second], now);

    private static PendingHostFileReconciliationRunner CreateRunner(
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor,
        IReadOnlyList<ProbeStorage> providers,
        DateTimeOffset? now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now ?? DateTimeOffset.UtcNow);
        return new PendingHostFileReconciliationRunner(
            queryExecutor,
            commandExecutor,
            new FileStorageProviderRegistry(
                providers,
                Options.Create(new FileStorageOptions
                {
                    DefaultProviderKey = providers[0].ProviderKey,
                })),
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }),
            clock);
    }

    private static PendingHostFileReconciliationOptions EnabledOptions() =>
        new()
        {
            Enabled = true,
            BatchSize = 100,
            MaxBatchesPerRun = 10,
            MinimumAgeSeconds = 300,
            PollSeconds = 300,
        };

    private static bool HasFileId(object? value, Guid expected) =>
        value is not null
        && (Guid?)value.GetType().GetProperty("FileId")?.GetValue(value) == expected;

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        new FilesModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private sealed class ProbeStorage : IFileStorageProvider
    {
        private readonly bool _exists;
        private readonly Exception? _exception;

        public ProbeStorage(string providerKey, bool exists)
        {
            ProviderKey = providerKey;
            _exists = exists;
        }

        public ProbeStorage(string providerKey, Exception exception)
        {
            ProviderKey = providerKey;
            _exception = exception;
        }

        public string ProviderKey { get; }

        public int ProbeCount { get; private set; }

        public Task<bool> ExistsAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            ProbeCount++;
            return _exception is null
                ? Task.FromResult(_exists)
                : Task.FromException<bool>(_exception);
        }

        public Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob save was not expected.");

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob read was not expected.");

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob delete was not expected.");
    }

    private sealed class PendingQueryExecutor(
        params IReadOnlyList<PendingHostFileRecord>[] batches) : IQueryExecutor
    {
        private int _nextBatch;

        public int QueryCount { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Single-row query was not expected.");

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            if (typeof(T) != typeof(PendingHostFileRecord))
            {
                throw new InvalidOperationException("Unexpected query record type.");
            }

            IReadOnlyList<PendingHostFileRecord> batch = _nextBatch < batches.Length
                ? batches[_nextBatch++]
                : Array.Empty<PendingHostFileRecord>();
            return Task.FromResult((IReadOnlyList<T>)(object)batch);
        }
    }
}
