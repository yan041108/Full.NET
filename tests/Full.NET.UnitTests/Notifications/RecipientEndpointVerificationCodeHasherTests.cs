using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class RecipientEndpointVerificationCodeHasherTests
{
    [TestMethod]
    public void Hash_is_deterministic_for_same_challenge_and_code()
    {
        var challengeId = Guid.Parse("0198f36e-f7a7-7c52-9cbb-774e67411205");
        var first = RecipientEndpointVerificationCodeHasher.Hash(challengeId, "123456");
        var second = RecipientEndpointVerificationCodeHasher.Hash(challengeId, "123456");
        Assert.AreEqual(first, second);
        Assert.AreEqual(64, first.Length);
    }

    [TestMethod]
    public void Hash_changes_when_code_changes()
    {
        var challengeId = Guid.Parse("0198f36e-f7a7-7c52-9cbb-774e67411205");
        var first = RecipientEndpointVerificationCodeHasher.Hash(challengeId, "123456");
        var second = RecipientEndpointVerificationCodeHasher.Hash(challengeId, "654321");
        Assert.AreNotEqual(first, second);
    }
}
