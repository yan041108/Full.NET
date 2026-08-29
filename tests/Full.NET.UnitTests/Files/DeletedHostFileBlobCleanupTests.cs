using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files;
using Full.NET.Modules.Files.Cleanup;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class DeletedHostFileBlobCleanupTests
{
    [TestMethod]
    public void Background_registration_uses_disabled_defaults_and_rejects_unsafe_configuration()
    {
        using var defaults = CreateProvider(new Dictionary<string, string?>());
        var options = defaults.GetRequiredService<
            IOptions<DeletedHostFileBlobCleanupOptions>>().Value;

        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(100, options.BatchSize);
        Assert.AreEqual(10, options.MaxBatchesPerRun);
        Assert.AreEqual(300, options.PollSeconds);
        Assert.IsTrue(defaults.GetServices<IHostedService>()
            .Any(service => service is DeletedHostFileBlobCleanupHostedProcessor));

        using var invalid = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Files:Local:RootPath"] = "worker-files",
                ["Files:Cleanup:Enabled"] = "true",
                ["Files:Cleanup:BatchSize"] = "0",
                ["Files:Cleanup:MaxBatchesPerRun"] = "101",
                ["Files:Cleanup:PollSeconds"] = "4",
            });
        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);

        Assert.HasCount(3, exception.Failures);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "files.cleanup.select_deleted.sql_server")]
    [DataRow(DatabaseProvider.MySql, "files.cleanup.select_deleted.my_sql")]
    public async Task Runner_advances_past_blob_failures_and_purges_only_successes(
        DatabaseProvider provider,
        string expectedSelectStatement)
    {
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        var thirdId = Guid.CreateVersion7();
        var firstDeletedAtUtc = new DateTimeOffset(
            2026,
            7,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var secondDeletedAtUtc = firstDeletedAtUtc.AddMinutes(1);
        var thirdDeletedAtUtc = firstDeletedAtUtc.AddMinutes(2);
        var query = new RecordingQueryExecutor(
            [
                [
                    new DeletedHostFileBlobRecord(
                        firstId,
                        LocalHostFileBlobStorage.Key,
                        "host/2026/07/failure",
                        firstDeletedAtUtc),
                    new DeletedHostFileBlobRecord(
                        secondId,
                        LocalHostFileBlobStorage.Key,
                        "host/2026/07/second",
                        secondDeletedAtUtc),
                ],
                [
                    new DeletedHostFileBlobRecord(
                        thirdId,
                        LocalHostFileBlobStorage.Key,
                        "host/2026/07/third",
                        thirdDeletedAtUtc),
                ],
            ]);
        var sequence = new List<string>();
        var blobs = new RecordingBlobStorage(
            sequence,
            "host/2026/07/failure");
        var commands = new RecordingCommandExecutor(sequence, [1, 1]);
        var runner = CreateRunner(provider, query, commands, blobs);

        var result = await runner.RunOnceAsync(
            new DeletedHostFileBlobCleanupOptions
            {
                Enabled = true,
                BatchSize = 2,
                MaxBatchesPerRun = 3,
            },
            CancellationToken.None);

        Assert.AreEqual(3, result.Scanned);
        Assert.AreEqual(2, result.Purged);
        Assert.AreEqual(1, result.BlobFailures);
        Assert.AreEqual(0, result.ConcurrentlyCompleted);
        Assert.AreEqual(2, result.BatchesExecuted);
        CollectionAssert.AreEqual(
            new[] { expectedSelectStatement, expectedSelectStatement },
            query.Statements.Select(statement => statement.Name).ToArray());
        Assert.IsNull(ReadParameter<Guid?>(query.Parameters[0], "AfterId"));
        CollectionAssert.AreEqual(
            new[]
            {
                "blob:host/2026/07/failure",
                "blob:host/2026/07/second",
                $"db:{secondId:D}",
                "blob:host/2026/07/third",
                $"db:{thirdId:D}",
            },
            sequence);
        Assert.AreEqual(
            secondDeletedAtUtc,
            ReadParameter<DateTimeOffset>(
                query.Parameters[1],
                "AfterDeletedAtUtc"));
        Assert.AreEqual(
            secondId,
            ReadParameter<Guid>(query.Parameters[1], "AfterId"));
        Assert.AreEqual(
            1,
            ReadParameter<int>(query.Parameters[1], "HasCursor"));
    }

    [TestMethod]
    public async Task Runner_treats_a_concurrently_removed_tombstone_as_idempotent_completion()
    {
        var fileId = Guid.CreateVersion7();
        var query = new RecordingQueryExecutor(
            [
                [
                    new DeletedHostFileBlobRecord(
                        fileId,
                        LocalHostFileBlobStorage.Key,
                        "host/2026/07/concurrent",
                        new DateTimeOffset(
                            2026,
                            7,
                            1,
                            0,
                            0,
                            0,
                            TimeSpan.Zero)),
                ],
            ]);
        var sequence = new List<string>();
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            query,
            new RecordingCommandExecutor(sequence, [0]),
            new RecordingBlobStorage(sequence));

        var result = await runner.RunOnceAsync(
            EnabledOptions(),
            CancellationToken.None);

        Assert.AreEqual(1, result.Scanned);
        Assert.AreEqual(0, result.Purged);
        Assert.AreEqual(0, result.BlobFailures);
        Assert.AreEqual(1, result.ConcurrentlyCompleted);
    }

    [TestMethod]
    public async Task Runner_propagates_cancellation_without_purging_the_tombstone()
    {
        var fileId = Guid.CreateVersion7();
        var query = new RecordingQueryExecutor(
            [
                [
                    new DeletedHostFileBlobRecord(
                        fileId,
                        LocalHostFileBlobStorage.Key,
                        "host/2026/07/canceled",
                        new DateTimeOffset(
                            2026,
                            7,
                            1,
                            0,
                            0,
                            0,
                            TimeSpan.Zero)),
                ],
            ]);
        var sequence = new List<string>();
        var commands = new RecordingCommandExecutor(
            sequence,
            Array.Empty<int>());
        var blobs = new RecordingBlobStorage(
            sequence,
            canceledStorageKey: "host/2026/07/canceled");
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            query,
            commands,
            blobs);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunOnceAsync(
                EnabledOptions(),
                cancellation.Token));

        Assert.HasCount(0, commands.Statements);
    }

    [TestMethod]
    public async Task Runner_deletes_only_from_the_provider_recorded_on_the_tombstone()
    {
        var fileId = Guid.CreateVersion7();
        var deletedAtUtc = new DateTimeOffset(
            2026,
            8,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var query = new RecordingQueryExecutor(
            [
                [
                    new DeletedHostFileBlobRecord(
                        fileId,
                        "archive",
                        "host/2026/08/provider-boundary",
                        deletedAtUtc),
                ],
            ]);
        var sequence = new List<string>();
        var local = new RecordingBlobStorage(
            sequence,
            providerKey: LocalHostFileBlobStorage.Key);
        var archive = new RecordingBlobStorage(
            sequence,
            providerKey: "archive");
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            query,
            new RecordingCommandExecutor(sequence, [1]),
            local,
            archive);

        var result = await runner.RunOnceAsync(
            EnabledOptions(),
            CancellationToken.None);

        Assert.AreEqual(1, result.Purged);
        Assert.HasCount(0, local.DeletedStorageKeys);
        CollectionAssert.AreEqual(
            new[] { "host/2026/08/provider-boundary" },
            archive.DeletedStorageKeys);
    }

    [TestMethod]
    public async Task Disabled_processor_does_not_create_a_scope()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = new StaticCleanupOptionsMonitor(
            new DeletedHostFileBlobCleanupOptions());
        var processor = new DeletedHostFileBlobCleanupHostedProcessor(
            scopeFactory,
            options,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }),
            NullLogger<DeletedHostFileBlobCleanupHostedProcessor>.Instance);

        var result = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(0, result.Scanned);
        scopeFactory.DidNotReceive().CreateScope();
    }

    private static DeletedHostFileBlobCleanupOptions EnabledOptions() =>
        new()
        {
            Enabled = true,
            BatchSize = 100,
            MaxBatchesPerRun = 10,
        };

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        environment.ContentRootPath.Returns(Path.GetFullPath("."));
        services.AddSingleton(environment);
        new FilesModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private static DeletedHostFileBlobCleanupRunner CreateRunner(
        DatabaseProvider provider,
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor,
        params IFileStorageProvider[] storageProviders) =>
        new(
            queryExecutor,
            commandExecutor,
            new FileStorageProviderRegistry(
                storageProviders,
                Options.Create(new FileStorageOptions
                {
                    DefaultProviderKey = storageProviders[0].ProviderKey,
                })),
            Options.Create(new DatabaseOptions { Provider = provider }));

    private static T ReadParameter<T>(object parameters, string name)
    {
        if (parameters is IReadOnlyDictionary<string, object?> dictionary
            && dictionary.TryGetValue(name, out var dictionaryValue))
        {
            return dictionaryValue is null ? default! : (T)dictionaryValue;
        }

        var property = parameters.GetType().GetProperty(name)
            ?? throw new InvalidOperationException($"{name} is missing.");
        var propertyValue = property.GetValue(parameters);
        return propertyValue is null ? default! : (T)propertyValue;
    }

    private sealed class RecordingQueryExecutor(
        Queue<IReadOnlyList<DeletedHostFileBlobRecord>> pages)
        : IQueryExecutor
    {
        public RecordingQueryExecutor(
            IReadOnlyList<IReadOnlyList<DeletedHostFileBlobRecord>> pages)
            : this(new Queue<IReadOnlyList<DeletedHostFileBlobRecord>>(pages))
        {
        }

        public List<SqlStatement> Statements { get; } = [];

        public List<object> Parameters { get; } = [];

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                $"Unexpected single-row statement '{statement.Name}'.");

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Statements.Add(statement);
            Parameters.Add(parameters
                ?? throw new InvalidOperationException("Parameters are required."));
            return Task.FromResult<IReadOnlyList<T>>(
                pages.Dequeue().Cast<T>().ToArray());
        }
    }

    private sealed class RecordingCommandExecutor(
        List<string> sequence,
        Queue<int> results) : ICommandExecutor
    {
        public RecordingCommandExecutor(
            List<string> sequence,
            IReadOnlyList<int> results)
            : this(sequence, new Queue<int>(results))
        {
        }

        public List<SqlStatement> Statements { get; } = [];

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Statements.Add(statement);
            var fileId = ReadParameter<Guid>(
                parameters
                    ?? throw new InvalidOperationException("Parameters are required."),
                "FileId");
            sequence.Add($"db:{fileId:D}");
            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed class RecordingBlobStorage(
        List<string> sequence,
        string? failedStorageKey = null,
        string? canceledStorageKey = null,
        string providerKey = LocalHostFileBlobStorage.Key) : IFileStorageProvider
    {
        public string ProviderKey => providerKey;

        public List<string> DeletedStorageKeys { get; } = [];

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Save is not used by cleanup tests.");

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Read is not used by cleanup tests.");

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            sequence.Add($"blob:{storageKey}");
            DeletedStorageKeys.Add(storageKey);
            if (string.Equals(
                    storageKey,
                    canceledStorageKey,
                    StringComparison.Ordinal))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (string.Equals(
                    storageKey,
                    failedStorageKey,
                    StringComparison.Ordinal))
            {
                throw new IOException("Simulated blob deletion failure.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class StaticCleanupOptionsMonitor(
        DeletedHostFileBlobCleanupOptions options)
        : IOptionsMonitor<DeletedHostFileBlobCleanupOptions>
    {
        public DeletedHostFileBlobCleanupOptions CurrentValue => options;

        public DeletedHostFileBlobCleanupOptions Get(string? name) => options;

        public IDisposable? OnChange(
            Action<DeletedHostFileBlobCleanupOptions, string?> listener) =>
            null;
    }
}
