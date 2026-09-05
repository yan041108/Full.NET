using Full.NET.Modules.Identity.Features.ManageHostUsers;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserSensitiveFieldMaskerTests
{
    [TestMethod]
    public void MaskPhoneNumber_keeps_only_last_four_digits()
    {
        Assert.AreEqual("****0000", HostUserSensitiveFieldMasker.MaskPhoneNumber("+8613800000000"));
    }

    [TestMethod]
    public void MaskIdCardNumber_preserves_head_and_tail()
    {
        Assert.AreEqual(
            "110***********1234",
            HostUserSensitiveFieldMasker.MaskIdCardNumber("110101199001011234"));
    }

    [TestMethod]
    public void LooksLikeMasked_rejects_masked_placeholders_for_writes()
    {
        Assert.IsTrue(HostUserSensitiveFieldMasker.LooksLikeMaskedPhoneNumber("****0000"));
        Assert.IsTrue(HostUserSensitiveFieldMasker.LooksLikeMaskedIdCardNumber("110***********1234"));
        Assert.IsFalse(HostUserSensitiveFieldMasker.LooksLikeMaskedPhoneNumber("+8613800000000"));
    }
}
