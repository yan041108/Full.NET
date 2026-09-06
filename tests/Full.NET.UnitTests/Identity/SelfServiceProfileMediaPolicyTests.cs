using Full.NET.Modules.Identity.Features.SelfServiceProfile;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class SelfServiceProfileMediaPolicyTests
{
    [TestMethod]
    public void Avatar_content_types_allow_common_image_formats()
    {
        Assert.IsTrue(SelfServiceProfileMediaPolicy.IsAllowedAvatarContentType("image/jpeg"));
        Assert.IsTrue(SelfServiceProfileMediaPolicy.IsAllowedAvatarContentType("image/png"));
        Assert.IsTrue(SelfServiceProfileMediaPolicy.IsAllowedAvatarContentType("image/webp"));
        Assert.IsFalse(SelfServiceProfileMediaPolicy.IsAllowedAvatarContentType("application/pdf"));
    }

    [TestMethod]
    public void Signature_content_types_are_stricter_than_avatar()
    {
        Assert.IsTrue(SelfServiceProfileMediaPolicy.IsAllowedSignatureContentType("image/png"));
        Assert.IsFalse(SelfServiceProfileMediaPolicy.IsAllowedSignatureContentType("image/webp"));
    }
}
