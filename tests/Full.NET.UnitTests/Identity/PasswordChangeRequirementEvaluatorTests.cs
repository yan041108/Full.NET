namespace Full.NET.UnitTests.Identity;

using Full.NET.Modules.Identity.Security;

[TestClass]
public sealed class PasswordChangeRequirementEvaluatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Must_change_password_flag_requires_change()
    {
        Assert.IsTrue(
            PasswordChangeRequirementEvaluator.IsRequired(
                mustChangePassword: true,
                passwordChangedAtUtc: Now,
                Now,
                passwordExpirationDays: 0));
    }

    [TestMethod]
    public void Expired_password_requires_change_when_policy_enabled()
    {
        Assert.IsTrue(
            PasswordChangeRequirementEvaluator.IsRequired(
                mustChangePassword: false,
                passwordChangedAtUtc: Now.AddDays(-91),
                Now,
                passwordExpirationDays: 90));
    }

    [TestMethod]
    public void Fresh_password_does_not_require_change()
    {
        Assert.IsFalse(
            PasswordChangeRequirementEvaluator.IsRequired(
                mustChangePassword: false,
                passwordChangedAtUtc: Now.AddDays(-10),
                Now,
                passwordExpirationDays: 90));
    }

    [TestMethod]
    public void Missing_password_changed_timestamp_requires_change_when_policy_enabled()
    {
        Assert.IsTrue(
            PasswordChangeRequirementEvaluator.IsRequired(
                mustChangePassword: false,
                passwordChangedAtUtc: null,
                Now,
                passwordExpirationDays: 30));
    }
}
