using Full.NET.Hosting.Observability;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class LogQueueByteBudgetTests
{
    [TestMethod]
    public void Reservations_include_in_flight_bytes_until_explicit_release()
    {
        var budget = new LogQueueByteBudget(128);

        Assert.IsTrue(budget.TryReserve(96));
        Assert.IsFalse(budget.TryReserve(40));
        Assert.IsTrue(budget.TryReserve(32));
        Assert.AreEqual(128, budget.ReservedBytes);

        budget.Release(96);
        Assert.AreEqual(32, budget.ReservedBytes);
        Assert.IsTrue(budget.TryReserve(64));
        Assert.AreEqual(96, budget.ReservedBytes);
    }

    [TestMethod]
    public void Independent_budgets_cannot_borrow_capacity()
    {
        var general = new LogQueueByteBudget(16);
        var highPriority = new LogQueueByteBudget(16);

        Assert.IsTrue(general.TryReserve(16));
        Assert.IsFalse(general.TryReserve(1));
        Assert.IsTrue(highPriority.TryReserve(16));
        Assert.AreEqual(16, general.ReservedBytes);
        Assert.AreEqual(16, highPriority.ReservedBytes);
    }

    [TestMethod]
    public void Concurrent_reservations_never_exceed_capacity_and_return_to_zero()
    {
        var budget = new LogQueueByteBudget(64);
        var accepted = 0;

        Parallel.For(0, 1_000, _ =>
        {
            if (budget.TryReserve(8))
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.AreEqual(8, accepted);
        Assert.AreEqual(64, budget.ReservedBytes);
        Parallel.For(0, accepted, _ => budget.Release(8));
        Assert.AreEqual(0, budget.ReservedBytes);
    }

    [TestMethod]
    public void Invalid_or_duplicate_release_cannot_make_budget_negative()
    {
        var budget = new LogQueueByteBudget(16);
        Assert.IsTrue(budget.TryReserve(8));

        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Release(16));
        Assert.AreEqual(8, budget.ReservedBytes);
        budget.Release(8);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Release(8));
        Assert.AreEqual(0, budget.ReservedBytes);
    }
}
