using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>MySQL 上的投递租约争抢、到期重领与旧代次 fencing 回归。</summary>
[TestClass]
public sealed class NotificationDeliveryLeasePersistenceMySqlTests
{
    [TestMethod]
    public Task Concurrent_claim_admits_only_one_owner() =>
        NotificationDeliveryLeasePersistenceAssertions.Concurrent_claim_admits_only_one_owner_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Expired_lease_can_be_reclaimed_and_old_generation_is_fenced() =>
        NotificationDeliveryLeasePersistenceAssertions.Expired_lease_can_be_reclaimed_and_old_generation_is_fenced_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Renew_requires_current_generation_and_revision() =>
        NotificationDeliveryLeasePersistenceAssertions.Renew_requires_current_generation_and_revision_async(DatabaseProvider.MySql);
}
