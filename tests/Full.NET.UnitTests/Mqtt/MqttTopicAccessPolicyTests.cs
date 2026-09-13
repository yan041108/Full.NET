using Full.NET.Modules.Mqtt.Security;

namespace Full.NET.UnitTests.Mqtt;

/// <summary>MQTT 主题 ACL 与发布速率限制单元测试。</summary>
[TestClass]
public sealed class MqttTopicAccessPolicyTests
{
    private static readonly string[] AllowedPrefixes = ["fullnet/", "tenants/"];

    [TestMethod]
    public void CanPublish_allows_host_fullnet_topics()
    {
        Assert.IsTrue(MqttTopicAccessPolicy.CanPublish(
            "fullnet/control/ping",
            isHost: true,
            tenantId: null,
            AllowedPrefixes));
    }

    [TestMethod]
    public void CanPublish_allows_host_tenant_scoped_topics()
    {
        var tenantId = Guid.Parse("01956000-0001-7000-8000-000000000099");
        Assert.IsTrue(MqttTopicAccessPolicy.CanPublish(
            $"tenants/{tenantId}/events/demo",
            isHost: true,
            tenantId: null,
            AllowedPrefixes));
    }

    [TestMethod]
    public void CanPublish_restricts_tenant_to_own_prefix()
    {
        var tenantId = Guid.Parse("01956000-0001-7000-8000-000000000099");
        var otherTenantId = Guid.Parse("01956000-0001-7000-8000-000000000088");
        Assert.IsTrue(MqttTopicAccessPolicy.CanPublish(
            $"tenants/{tenantId}/events/demo",
            isHost: false,
            tenantId,
            AllowedPrefixes));
        Assert.IsFalse(MqttTopicAccessPolicy.CanPublish(
            $"tenants/{otherTenantId}/events/demo",
            isHost: false,
            tenantId,
            AllowedPrefixes));
        Assert.IsFalse(MqttTopicAccessPolicy.CanPublish(
            "fullnet/control/ping",
            isHost: false,
            tenantId,
            AllowedPrefixes));
    }

    [TestMethod]
    public void CanPublish_rejects_wildcards_and_unknown_prefix()
    {
        Assert.IsFalse(MqttTopicAccessPolicy.CanPublish(
            "fullnet/demo/#",
            isHost: true,
            tenantId: null,
            AllowedPrefixes));
        Assert.IsFalse(MqttTopicAccessPolicy.CanPublish(
            "kafka/events/demo",
            isHost: true,
            tenantId: null,
            AllowedPrefixes));
    }
}

/// <summary>MQTT 发布速率限制器单元测试。</summary>
[TestClass]
public sealed class MqttPublishRateLimiterTests
{
    [TestMethod]
    public void TryAcquire_removes_inactive_user_windows()
    {
        var limiter = new MqttPublishRateLimiter();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 100; i++)
            Assert.IsTrue(limiter.TryAcquire(Guid.CreateVersion7(), 2, now));
        Assert.IsTrue(limiter.TryAcquire(Guid.CreateVersion7(), 2, now.AddMinutes(2)));
        var entries = typeof(MqttPublishRateLimiter)
            .GetField("_windows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(limiter)!;
        Assert.AreEqual(1, (int)entries.GetType().GetProperty("Count")!.GetValue(entries)!);
    }

    [TestMethod]
    public void TryAcquire_bounds_users_without_evicting_active_quota()
    {
        var limiter = new MqttPublishRateLimiter();
        var now = DateTimeOffset.UtcNow;
        var first = Guid.CreateVersion7();
        Assert.IsTrue(limiter.TryAcquire(first, 1, now));
        for (var i = 1; i < 10000; i++)
            Assert.IsTrue(limiter.TryAcquire(Guid.CreateVersion7(), 1, now));
        Assert.IsFalse(limiter.TryAcquire(Guid.CreateVersion7(), 1, now));
        Assert.IsFalse(limiter.TryAcquire(first, 1, now));
        Assert.IsTrue(limiter.TryAcquire(Guid.CreateVersion7(), 1, now.AddMinutes(2)));
    }

    [TestMethod]
    public void TryAcquire_preserves_quota_under_concurrent_cleanup()
    {
        var limiter = new MqttPublishRateLimiter();
        var user = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        limiter.TryAcquire(Guid.CreateVersion7(), 1, now.AddMinutes(-2));
        var accepted = 0;
        Parallel.For(0, 1000, _ =>
        {
            if (limiter.TryAcquire(user, 10, now)) Interlocked.Increment(ref accepted);
        });
        Assert.AreEqual(10, accepted);
    }

    [TestMethod]
    public void TryAcquire_enforces_per_user_minute_window()
    {
        var limiter = new MqttPublishRateLimiter();
        var userId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue(limiter.TryAcquire(userId, 2, now));
        Assert.IsTrue(limiter.TryAcquire(userId, 2, now.AddSeconds(1)));
        Assert.IsFalse(limiter.TryAcquire(userId, 2, now.AddSeconds(2)));
        Assert.IsTrue(limiter.TryAcquire(userId, 2, now.AddMinutes(1).AddSeconds(1)));
    }
}
