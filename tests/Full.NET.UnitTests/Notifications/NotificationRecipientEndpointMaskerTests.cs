using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationRecipientEndpointMaskerTests
{
    [TestMethod]
    public void Email_and_phone_are_masked_without_raw_value()
    {
        Assert.AreEqual("a***@***.com", NotificationRecipientEndpointMasker.Mask("alice@example.com", "email"));
        Assert.AreEqual("****5678", NotificationRecipientEndpointMasker.Mask("13800135678", "sms"));
        Assert.IsFalse(
            NotificationRecipientEndpointMasker.Mask("secret-openid", "wecom")
                .Contains("secret-openid", StringComparison.Ordinal));
    }
}
