using Full.NET.Logging.Kafka;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class KafkaLogProducerBudgetTests
{
    [TestMethod]
    public void Admission_limits_messages_and_bytes_until_delivery_completion()
    {
        var budget = new KafkaLogProducerBudget(maxMessages: 2, maxBytes: 400);

        Assert.IsTrue(budget.TryReserve(50, out var first));
        Assert.IsTrue(budget.TryReserve(50, out var second));
        Assert.IsFalse(budget.TryReserve(50, out _));
        Assert.AreEqual(2, budget.ReservedMessages);
        Assert.AreEqual(356, budget.ReservedBytes);

        first!.Dispose();
        Assert.IsTrue(budget.TryReserve(50, out var third));
        second!.Dispose();
        third!.Dispose();
        Assert.AreEqual(0, budget.ReservedMessages);
        Assert.AreEqual(0, budget.ReservedBytes);
    }

    [TestMethod]
    public void Byte_limit_rejects_oversize_message_and_double_release_is_idempotent()
    {
        var budget = new KafkaLogProducerBudget(maxMessages: 10, maxBytes: 300);

        Assert.IsFalse(budget.TryReserve(173, out _));
        Assert.IsTrue(budget.TryReserve(172, out var accepted));
        Assert.IsFalse(budget.TryReserve(0, out _));
        accepted!.Dispose();
        accepted.Dispose();
        Assert.AreEqual(0, budget.ReservedMessages);
        Assert.AreEqual(0, budget.ReservedBytes);
    }

    [TestMethod]
    public void Concurrent_admission_never_exceeds_either_limit()
    {
        var budget = new KafkaLogProducerBudget(maxMessages: 8, maxBytes: 1_500);
        var reservations = new System.Collections.Concurrent.ConcurrentBag<KafkaLogProducerBudget.Reservation>();

        Parallel.For(0, 1_000, _ =>
        {
            if (budget.TryReserve(100, out var reservation))
            {
                reservations.Add(reservation!);
            }
        });

        Assert.IsLessThanOrEqualTo(8, budget.ReservedMessages);
        Assert.IsLessThanOrEqualTo(1_500, budget.ReservedBytes);
        Assert.AreEqual(6, reservations.Count);
        Parallel.ForEach(reservations, reservation => reservation.Dispose());
        Assert.AreEqual(0, budget.ReservedMessages);
        Assert.AreEqual(0, budget.ReservedBytes);
    }

    [TestMethod]
    public void Concurrent_delivery_and_failure_cleanup_release_one_reservation_only_once()
    {
        var budget = new KafkaLogProducerBudget(maxMessages: 1, maxBytes: 200);
        Assert.IsTrue(budget.TryReserve(50, out var reservation));
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        var releasers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            reservation!.Dispose();
        })).ToArray();

        var bothReady = ready.Wait(TimeSpan.FromSeconds(5));
        start.Set();
        Assert.IsTrue(bothReady);
        Task.WaitAll(releasers);
        Assert.AreEqual(0, budget.ReservedMessages);
        Assert.AreEqual(0, budget.ReservedBytes);
        Assert.IsTrue(budget.TryReserve(50, out var next));
        Assert.IsFalse(budget.TryReserve(50, out _));
        next!.Dispose();
    }
}
