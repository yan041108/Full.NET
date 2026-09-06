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
