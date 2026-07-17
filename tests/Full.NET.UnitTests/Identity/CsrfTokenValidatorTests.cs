using Full.NET.Modules.Identity.Security;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class CsrfTokenValidatorTests
{
    [TestMethod]
    public void Equal_nonempty_tokens_are_accepted()
    {
        Assert.IsTrue(CsrfTokenValidator.IsValid("csrf-token", "csrf-token"));
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", "")]
    [DataRow("csrf-token", null)]
    [DataRow(null, "csrf-token")]
    [DataRow("csrf-token", "csrf-tampered")]
    public void Missing_or_different_tokens_are_rejected(string? cookie, string? header)
    {
        Assert.IsFalse(CsrfTokenValidator.IsValid(cookie, header));
    }
}
