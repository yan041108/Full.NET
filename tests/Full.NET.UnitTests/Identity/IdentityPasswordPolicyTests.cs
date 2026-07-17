using Full.NET.Modules.Identity.Security;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityPasswordPolicyTests
{
    [TestMethod]
    public void Strong_password_is_accepted()
    {
        var violations = IdentityPasswordPolicy.Validate("FullNet!2026Secure");

        Assert.HasCount(0, violations);
    }

    [TestMethod]
    [DataRow("Short!1A")]
    [DataRow("FULLNET!2026SECURE")]
    [DataRow("fullnet!2026secure")]
    [DataRow("FullNetSecureOnly!")]
    [DataRow("FullNet2026Secure")]
    public void Weak_password_is_rejected(string password)
    {
        var violations = IdentityPasswordPolicy.Validate(password);

        Assert.IsGreaterThan(0, violations.Count);
    }
}
