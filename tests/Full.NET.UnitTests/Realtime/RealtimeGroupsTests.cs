using Full.NET.Realtime;

namespace Full.NET.UnitTests.Realtime;

[TestClass]
public sealed class RealtimeGroupsTests
{
    [TestMethod]
    public void User_group_uses_lowercase_guid_format()
    {
        var userId = Guid.Parse("01912345-6789-7abc-8def-0123456789ab");
        Assert.AreEqual("user:01912345-6789-7abc-8def-0123456789ab", RealtimeGroups.User(userId));
    }

    [TestMethod]
    public void Tenant_group_uses_lowercase_guid_format()
    {
        var tenantId = Guid.Parse("01912345-6789-7abc-8def-0123456789cd");
        Assert.AreEqual("tenant:01912345-6789-7abc-8def-0123456789cd", RealtimeGroups.Tenant(tenantId));
    }
}
