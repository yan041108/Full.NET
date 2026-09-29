using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Auditing.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditDetailsCapturePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Capture_stays_disabled_without_fresh_authoritative_checkpoint()
    {
        var clock = new MutableClock { UtcNow = Now };
        var monitor = new StaticOptionsMonitor(new AuditDetailsCaptureOptions { Enabled = true });
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);

        Assert.IsFalse(cache.CanCapture());
        cache.RecordSuccessfulRead(null);
        Assert.IsFalse(cache.CanCapture());
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        Assert.IsTrue(cache.CanCapture());

        clock.UtcNow = Now.AddSeconds(91);
        Assert.IsFalse(cache.CanCapture());
        clock.UtcNow = Now;
        cache.Clear();
        Assert.IsFalse(cache.CanCapture());
    }

    [TestMethod]
    public void Capture_rejects_stale_future_and_overdue_backlog_snapshots()
    {
        var clock = new MutableClock { UtcNow = Now };
        var cache = new AuditDetailsCapturePolicyCache(
            clock, new StaticOptionsMonitor(new AuditDetailsCaptureOptions { Enabled = true }));

        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(
            Now.AddSeconds(-181), null));
        Assert.IsFalse(cache.CanCapture());
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(
            Now.AddSeconds(1), null));
        Assert.IsFalse(cache.CanCapture());
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(
            Now, Now.AddSeconds(-301)));
        Assert.IsFalse(cache.CanCapture());
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(
            Now, Now.AddSeconds(1)));
        Assert.IsFalse(cache.CanCapture());
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(
            Now, Now.AddSeconds(-30)));
        Assert.IsTrue(cache.CanCapture());
    }

    [TestMethod]
    public void Disabled_or_invalid_hot_options_fail_closed()
    {
        var clock = new MutableClock { UtcNow = Now };
        var monitor = new StaticOptionsMonitor(new AuditDetailsCaptureOptions { Enabled = false });
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        Assert.IsFalse(cache.CanCapture());
        monitor.ThrowOnRead = true;
        Assert.IsFalse(cache.CanCapture());
    }

    [TestMethod]
    public void Capture_options_reject_unbounded_or_inverted_refresh_windows()
    {
        var result = new AuditDetailsCaptureOptionsValidator().Validate(null,
            new AuditDetailsCaptureOptions
            {
                RefreshSeconds = 0,
                MaxCacheAgeSeconds = 1,
                MaxCheckpointAgeSeconds = 1,
                MaxCleanupLagSeconds = 0,
            });

        Assert.IsTrue(result.Failed);
        Assert.HasCount(4, result.Failures);
    }

    [TestMethod]
    public async Task Background_refresh_uses_host_scope_and_revokes_on_read_failure_or_disable()
    {
        var clock = new MutableClock { UtcNow = Now };
        var options = new AuditDetailsCaptureOptions { Enabled = true };
        var monitor = new StaticOptionsMonitor(options);
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        var readerState = new ReaderState
        {
            Snapshot = new AuditDetailsCleanupCheckpointSnapshot(Now, null),
        };
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<IAuditDetailsCleanupCheckpointReader>(provider =>
            new FakeReader(readerState, provider.GetRequiredService<ICurrentTenant>()));
        await using var provider = services.BuildServiceProvider();
        var refresher = new AuditDetailsCapturePolicyRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(), cache, monitor,
            NullLogger<AuditDetailsCapturePolicyRefreshService>.Instance);

        await refresher.RefreshOnceAsync(CancellationToken.None);
        Assert.IsTrue(readerState.SawHost);
        Assert.IsTrue(cache.CanCapture());
        readerState.Throw = true;
        await refresher.RefreshOnceAsync(CancellationToken.None);
        Assert.IsFalse(cache.CanCapture());
        Assert.AreEqual(2, readerState.ReadCount);

        readerState.Throw = false;
        options.Enabled = false;
        await refresher.RefreshOnceAsync(CancellationToken.None);
        Assert.AreEqual(2, readerState.ReadCount);
        Assert.IsFalse(cache.CanCapture());
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class StaticOptionsMonitor(AuditDetailsCaptureOptions options)
        : IOptionsMonitor<AuditDetailsCaptureOptions>
    {
        public bool ThrowOnRead { get; set; }

        public AuditDetailsCaptureOptions CurrentValue => ThrowOnRead
            ? throw new OptionsValidationException("capture", typeof(AuditDetailsCaptureOptions),
                ["invalid"])
            : options;

        public AuditDetailsCaptureOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuditDetailsCaptureOptions, string?> listener) => null;
    }

    private sealed class ReaderState
    {
        public AuditDetailsCleanupCheckpointSnapshot? Snapshot { get; set; }

        public bool Throw { get; set; }

        public bool SawHost { get; set; }

        public int ReadCount { get; set; }
    }

    private sealed class FakeReader(ReaderState state, ICurrentTenant tenant)
        : IAuditDetailsCleanupCheckpointReader
    {
        public Task<AuditDetailsCleanupCheckpointSnapshot?> ReadAsync(
            CancellationToken cancellationToken)
        {
            state.ReadCount++;
            state.SawHost = tenant.IsHost;
            if (state.Throw)
            {
                throw new InvalidOperationException("read failed");
            }

            return Task.FromResult(state.Snapshot);
        }
    }
}
