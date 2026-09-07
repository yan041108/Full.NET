using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 上的配额预留并发、跨月与未知结算回归。</summary>
[TestClass]
public sealed class AiQuotaReservationPersistenceMySqlTests
{
    [TestMethod]
    public Task Concurrent_reserve_on_last_tokens_admits_only_one() =>
        AiQuotaReservationPersistenceAssertions.Concurrent_reserve_on_last_tokens_admits_only_one_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Cross_month_reserve_resets_counters() =>
        AiQuotaReservationPersistenceAssertions.Cross_month_reserve_resets_counters_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Unknown_settle_does_not_refund_reserved_tokens() =>
        AiQuotaReservationPersistenceAssertions.Unknown_settle_does_not_refund_reserved_tokens_async(DatabaseProvider.MySql);
}
