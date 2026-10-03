using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Auditing.Retention;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingRetentionTests
{
    [TestMethod]
    public void Background_registration_uses_disabled_defaults_and_rejects_unsafe_bounds()
    {
        using var defaults = CreateProvider(
            new Dictionary<string, string?>());
        var options = defaults.GetRequiredService<
            IOptions<AuditingRetentionOptions>>().Value;

        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(30, options.AccessRetentionDays);
        Assert.AreEqual(365, options.OperationRetentionDays);
        Assert.AreEqual(90, options.ExceptionRetentionDays);
        Assert.AreEqual(90, options.OutboundRetentionDays);
        Assert.AreEqual(200, options.BatchSize);
        Assert.AreEqual(15, options.MaxBatchesPerRun);
        Assert.AreEqual(3600, options.PollSeconds);

        using var invalid = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Auditing:Retention:AccessRetentionDays"] = "0",
                ["Auditing:Retention:OperationRetentionDays"] = "3651",
                ["Auditing:Retention:ExceptionRetentionDays"] = "0",
                ["Auditing:Retention:OutboundRetentionDays"] = "0",
                ["Auditing:Retention:BatchSize"] = "2001",
                ["Auditing:Retention:MaxBatchesPerRun"] = "0",
                ["Auditing:Retention:PollSeconds"] = "59",
            });
        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);

        Assert.AreEqual(7, exception.Failures.Count());
    }

    [TestMethod]
    public void Details_retention_options_remain_active_when_summary_retention_is_disabled()
    {
        var services = new ServiceCollection();
        new AuditingModule().AddBackgroundServices(
            services,
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(AuditDetailsRetentionHostedService)));

        using var defaults = CreateProvider(new Dictionary<string, string?>());
        Assert.IsFalse(defaults.GetRequiredService<IOptions<AuditingRetentionOptions>>().Value.Enabled);
        var details = defaults.GetRequiredService<IOptions<AuditDetailsRetentionOptions>>().Value;
        Assert.AreEqual(200, details.BatchSize);
        Assert.AreEqual(15, details.MaxBatchesPerRun);
        Assert.AreEqual(60, details.PollSeconds);

        using var invalid = CreateProvider(new Dictionary<string, string?>
        {
            ["Auditing:DetailsRetention:BatchSize"] = "0",
            ["Auditing:DetailsRetention:MaxBatchesPerRun"] = "101",
            ["Auditing:DetailsRetention:PollSeconds"] = "59",
        });
        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);
        Assert.AreEqual(3, exception.Failures.Count());
    }

    [TestMethod]
    public async Task Disabled_retention_does_not_touch_the_database()
    {
        var query = new RecordingQueryExecutor();
        var command = new RecordingCommandExecutor();
        var transaction = new RecordingTransaction();
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            query,
            command,
            transaction);

        var result = await runner.RunOnceAsync(
            new AuditingRetentionOptions { Enabled = false },
            CancellationToken.None);

        Assert.AreEqual(0, result.TotalDeleted);
        Assert.AreEqual(0, result.BatchesExecuted);
        Assert.AreEqual(0, query.Statements.Count);
        Assert.AreEqual(0, command.Statements.Count);
        Assert.AreEqual(0, transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task SqlServer_details_cleanup_clears_expired_context_even_when_summary_retention_is_disabled()
    {
        var query = new RecordingQueryExecutor();
        var command = new RecordingCommandExecutor(
            new Dictionary<string, Queue<int>>(StringComparer.Ordinal)
            {
                ["auditing.details.clear_expired.sql_server"] = new Queue<int>([2, 1]),
            });
        var runner = new AuditDetailsRetentionRunner(
            query, command, new RecordingTransaction(), new FixedClock(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        var result = await runner.RunOnceAsync(
            new AuditDetailsRetentionOptions { BatchSize = 2, MaxBatchesPerRun = 3 },
            CancellationToken.None);

        Assert.AreEqual(3, result.Cleared);
        Assert.AreEqual(2, result.BatchesExecuted);
        Assert.AreEqual(2, command.Statements.Count);
        Assert.IsTrue(command.Statements.All(statement =>
            statement.Text.Contains("ContextJson = NULL", StringComparison.Ordinal)
            && statement.Text.Contains("DetailsExpiresAtUtc = NULL", StringComparison.Ordinal)
            && statement.Text.Contains("DetailsExpiresAtUtc <= @NowUtc", StringComparison.Ordinal)));
        Assert.IsFalse(result.MayHaveMore);
    }

    [TestMethod]
    public async Task Details_cleanup_retries_immediately_after_bounded_full_batch()
    {
        var command = new RecordingCommandExecutor(
            new Dictionary<string, Queue<int>>(StringComparer.Ordinal)
            {
                ["auditing.details.clear_expired.sql_server"] = new Queue<int>([2]),
            });
        var runner = new AuditDetailsRetentionRunner(
            new RecordingQueryExecutor(), command, new RecordingTransaction(), new FixedClock(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        var result = await runner.RunOnceAsync(
            new AuditDetailsRetentionOptions { BatchSize = 2, MaxBatchesPerRun = 1 },
            CancellationToken.None);

        Assert.AreEqual(2, result.Cleared);
        Assert.IsTrue(result.MayHaveMore);
    }

    [TestMethod]
    public async Task MySql_details_cleanup_claims_expired_ids_then_clears_both_columns()
    {
        var id = Guid.CreateVersion7();
        var query = new RecordingQueryExecutor(
            new Dictionary<string, Queue<IReadOnlyList<Guid>>>(StringComparer.Ordinal)
            {
                ["auditing.details.select_expired_ids.my_sql"] =
                    new Queue<IReadOnlyList<Guid>>([[id]]),
            });
        var command = new RecordingCommandExecutor();
        var transaction = new RecordingTransaction();
        var runner = new AuditDetailsRetentionRunner(
            query, command, transaction, new FixedClock(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.MySql }));

        var result = await runner.RunOnceAsync(
            new AuditDetailsRetentionOptions { BatchSize = 2, MaxBatchesPerRun = 3 },
            CancellationToken.None);

        Assert.AreEqual(1, result.Cleared);
        Assert.AreEqual(1, result.BatchesExecuted);
        Assert.AreEqual(1, transaction.ExecutionCount);
        Assert.AreEqual("auditing.details.clear_claimed.my_sql", command.Statements.Single().Name);
        Assert.IsTrue(command.Statements.Single().Text.Contains("ContextJson = NULL", StringComparison.Ordinal));
        Assert.IsTrue(command.Statements.Single().Text.Contains("DetailsExpiresAtUtc = NULL", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { id }, ReadIds(command.Parameters.Single()));
    }

    [TestMethod]
    public async Task SqlServer_retention_rotates_categories_before_repeating_a_full_batch()
    {
        var query = new RecordingQueryExecutor();
        var command = new RecordingCommandExecutor(
            new Dictionary<string, Queue<int>>(StringComparer.Ordinal)
            {
                ["auditing.retention.delete_access.sql_server"] =
                    new Queue<int>([2, 2]),
                ["auditing.retention.delete_operation.sql_server"] =
                    new Queue<int>([1]),
                ["auditing.retention.delete_exception.sql_server"] =
                    new Queue<int>([0]),
                ["auditing.retention.delete_outbound.sql_server"] =
                    new Queue<int>([0]),
            });
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            query,
            command,
            new RecordingTransaction());

        var result = await runner.RunOnceAsync(
            new AuditingRetentionOptions
            {
                Enabled = true,
                AccessRetentionDays = 30,
                OperationRetentionDays = 365,
                ExceptionRetentionDays = 90,
                BatchSize = 2,
                MaxBatchesPerRun = 4,
            },
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "auditing.retention.delete_access.sql_server",
                "auditing.retention.delete_operation.sql_server",
                "auditing.retention.delete_exception.sql_server",
                "auditing.retention.delete_outbound.sql_server",
            },
            command.Statements.Select(statement => statement.Name).ToArray());
        Assert.AreEqual(2, result.AccessDeleted);
        Assert.AreEqual(1, result.OperationDeleted);
        Assert.AreEqual(0, result.ExceptionDeleted);
        Assert.AreEqual(0, result.OutboundDeleted);
        Assert.AreEqual(4, result.BatchesExecuted);
        Assert.AreEqual(
            new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero),
            ReadCutoff(command.Parameters[0]));
        Assert.AreEqual(
            new DateTimeOffset(2025, 7, 29, 0, 0, 0, TimeSpan.Zero),
            ReadCutoff(command.Parameters[1]));
    }

    [TestMethod]
    public async Task MySql_retention_claims_ids_in_short_transactions_and_deletes_only_claimed_ids()
    {
        var accessId = Guid.CreateVersion7();
        var query = new RecordingQueryExecutor(
            new Dictionary<string, Queue<IReadOnlyList<Guid>>>(StringComparer.Ordinal)
            {
                ["auditing.retention.select_access_ids.my_sql"] =
                    new Queue<IReadOnlyList<Guid>>([[accessId]]),
                ["auditing.retention.select_operation_ids.my_sql"] =
                    new Queue<IReadOnlyList<Guid>>([[]]),
                ["auditing.retention.select_exception_ids.my_sql"] =
                    new Queue<IReadOnlyList<Guid>>([[]]),
                ["auditing.retention.select_outbound_ids.my_sql"] =
                    new Queue<IReadOnlyList<Guid>>([[]]),
            });
        var command = new RecordingCommandExecutor();
        var transaction = new RecordingTransaction();
        var runner = CreateRunner(
            DatabaseProvider.MySql,
            query,
            command,
            transaction);

        var result = await runner.RunOnceAsync(
            new AuditingRetentionOptions
            {
                Enabled = true,
                BatchSize = 2,
                MaxBatchesPerRun = 10,
            },
            CancellationToken.None);

        Assert.AreEqual(4, transaction.ExecutionCount);
        Assert.AreEqual(1, result.AccessDeleted);
        Assert.AreEqual(1, result.TotalDeleted);
        Assert.AreEqual(
            "auditing.retention.delete_access_ids.my_sql",
            command.Statements.Single().Name);
        CollectionAssert.AreEqual(
            new[] { accessId },
            ReadIds(command.Parameters.Single()));
    }

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        new AuditingModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private static AuditingRetentionRunner CreateRunner(
        DatabaseProvider provider,
        IQueryExecutor query,
        ICommandExecutor command,
        ICommandTransaction transaction) =>
        new(
            query,
            command,
            transaction,
            new FixedClock(),
            Options.Create(new DatabaseOptions { Provider = provider }));

    private static DateTimeOffset ReadCutoff(object parameters) =>
        ReadSqlParameter<DateTimeOffset>(parameters, "CutoffUtc");

    private static Guid[] ReadIds(object parameters) =>
        ReadSqlParameter<Guid[]>(parameters, "Ids");

    private sealed class RecordingQueryExecutor(
        IReadOnlyDictionary<string, Queue<IReadOnlyList<Guid>>>? results = null)
        : IQueryExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

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
            Statements.Add(statement);
            var values = results?[statement.Name].Dequeue()
                ?? throw new InvalidOperationException(
                    $"No result configured for '{statement.Name}'.");
            return Task.FromResult<IReadOnlyList<T>>(
                values.Cast<T>().ToArray());
        }
    }

    private sealed class RecordingCommandExecutor(
        IReadOnlyDictionary<string, Queue<int>>? results = null)
        : ICommandExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public List<object> Parameters { get; } = [];

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            Parameters.Add(parameters
                ?? throw new InvalidOperationException("Parameters are required."));
            var affectedRows = results is null
                ? ReadIds(parameters).Length
                : results[statement.Name].Dequeue();
            return Task.FromResult(affectedRows);
        }
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return await action(cancellationToken);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
    }
}
