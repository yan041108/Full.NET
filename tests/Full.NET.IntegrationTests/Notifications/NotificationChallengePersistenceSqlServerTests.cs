using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>SQL Server 上的验证码失败计数上限与一次性消费回归。</summary>
[TestClass]
public sealed class NotificationChallengePersistenceSqlServerTests
{
    [TestMethod]
    public Task Concurrent_increment_stops_at_max_attempts() =>
        NotificationChallengePersistenceAssertions.Concurrent_increment_stops_at_max_attempts_async(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task Consume_is_one_shot_and_scope_bound() =>
        NotificationChallengePersistenceAssertions.Consume_is_one_shot_and_scope_bound_async(DatabaseProvider.SqlServer);
}
