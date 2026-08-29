using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Host.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Outbox;

[TestClass]
public sealed class OutboxRetentionTests
{
    [TestMethod]
    public async Task Disabled_defaults_reject_unsafe_bounds_and_do_not_create_a_scope()
    {
        var options = new OutboxRetentionOptions();
        var invalid = new OutboxRetentionOptions
        {
            RetentionDays = 0,
            BatchSize = 2001,
            MaxBatchesPerRun = 0,
            PollSeconds = 59,
        };
        var validation = new OutboxRetentionOptionsValidator()
            .Validate(Options.DefaultName, invalid);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var processor = CreateProcessor(
            scopeFactory,
            new MutableOptionsMonitor<OutboxRetentionOptions>(options));

        var result = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(30, options.RetentionDays);
        Assert.AreEqual(200, options.BatchSize);
        Assert.AreEqual(15, options.MaxBatchesPerRun);
        Assert.AreEqual(3600, options.PollSeconds);
        Assert.IsFalse(validation.Succeeded);
        Assert.AreEqual(4, validation.Failures?.Count());
        scopeFactory.DidNotReceive().CreateScope();
        Assert.AreEqual(OutboxRetentionResult.Empty, result);
    }

    [TestMethod]
    public async Task ProcessOnceAsync_stops_after_a_partial_batch_and_uses_one_strict_cutoff()
    {
        var now = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
        var options = new OutboxRetentionOptions
        {
            Enabled = true,
            RetentionDays = 30,
            BatchSize = 2,
            MaxBatchesPerRun = 10,
        };
        var store = Substitute.For<IOutboxRetentionStore>();
        store.DeleteProcessedBatchAsync(
                Arg.Any<DateTimeOffset>(),
                options.BatchSize,
                Arg.Any<CancellationToken>())
            .Returns(2, 1);
        await using var provider = CreateProvider(store);
        var processor = CreateProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MutableOptionsMonitor<OutboxRetentionOptions>(options),
            now);

        var result = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(3, result.DeletedRows);
        Assert.AreEqual(2, result.BatchesExecuted);
        await store.Received(2).DeleteProcessedBatchAsync(
            now.AddDays(-30),
            options.BatchSize,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ProcessOnceAsync_honors_reload_pause_before_starting_another_batch()
    {
        var current = new OutboxRetentionOptions
        {
            Enabled = true,
            BatchSize = 2,
            MaxBatchesPerRun = 10,
        };
        var monitor = new MutableOptionsMonitor<OutboxRetentionOptions>(current);
        var store = Substitute.For<IOutboxRetentionStore>();
        store.DeleteProcessedBatchAsync(
                Arg.Any<DateTimeOffset>(),
                current.BatchSize,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                monitor.CurrentValue = new OutboxRetentionOptions
                {
                    Enabled = false,
                };
                return 2;
            });
        await using var provider = CreateProvider(store);
        var processor = CreateProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            monitor);

        var result = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(2, result.DeletedRows);
        Assert.AreEqual(1, result.BatchesExecuted);
        await store.Received(1).DeleteProcessedBatchAsync(
            Arg.Any<DateTimeOffset>(),
            current.BatchSize,
            Arg.Any<CancellationToken>());
    }

    private static OutboxRetentionProcessor CreateProcessor(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<OutboxRetentionOptions> options,
        DateTimeOffset? now = null) =>
        new(
            scopeFactory,
            options,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }),
            new FixedClock(
                now
                ?? new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<OutboxRetentionProcessor>.Instance);

    private static ServiceProvider CreateProvider(IOutboxRetentionStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton(store);
        return services.BuildServiceProvider();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class MutableOptionsMonitor<T>(T currentValue)
        : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue { get; set; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
