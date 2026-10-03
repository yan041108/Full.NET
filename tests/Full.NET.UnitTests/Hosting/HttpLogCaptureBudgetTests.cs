using System.Collections.Concurrent;
using Full.NET.Hosting.Observability;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class HttpLogCaptureBudgetTests
{
    [TestMethod]
    public void Concurrent_reservations_do_not_exceed_event_or_byte_budget()
    {
        var budget = new HttpLogCaptureBudget(TimeProvider.System);
        var accepted = new ConcurrentBag<HttpLogCaptureBudget.Reservation>();

        Parallel.For(0, 1_000, _ =>
        {
            if (budget.TryReserve(16, 100, 1_600, out var reservation))
            {
                accepted.Add(reservation!);
            }
        });

        Assert.AreEqual(100, accepted.Count);
        foreach (var reservation in accepted)
        {
            reservation.Dispose();
        }
    }

    [TestMethod]
    public void Commit_refunds_unused_bytes_but_keeps_event_charge()
    {
        var budget = new HttpLogCaptureBudget(TimeProvider.System);
        Assert.IsTrue(budget.TryReserve(16, 2, 20, out var first));
        first!.Commit(4);

        Assert.IsTrue(budget.TryReserve(16, 2, 20, out var second));
        Assert.IsFalse(budget.TryReserve(16, 2, 20, out _));
        second!.Dispose();
    }

    [TestMethod]
    public void Old_lease_release_does_not_reduce_new_window_usage()
    {
        var clock = new ManualTimeProvider();
        var budget = new HttpLogCaptureBudget(clock);
        Assert.IsTrue(budget.TryReserve(10, 1, 10, out var oldLease));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(budget.TryReserve(10, 1, 10, out var currentLease));
        oldLease!.Dispose();

        Assert.IsFalse(budget.TryReserve(10, 1, 10, out _));
        currentLease!.Dispose();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, (long)duration.TotalMilliseconds);
    }
}
