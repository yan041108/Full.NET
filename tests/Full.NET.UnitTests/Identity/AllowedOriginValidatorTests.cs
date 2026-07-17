using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AllowedOriginValidatorTests
{
    [TestMethod]
    public void Same_origin_and_explicit_origin_are_accepted()
    {
        var validator = CreateValidator("https://admin.example.com");

        Assert.IsTrue(validator.IsAllowed(
            "https://api.example.com",
            "https://api.example.com"));
        Assert.IsTrue(validator.IsAllowed(
            "https://admin.example.com",
            "https://api.example.com"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("https://admin.example.com.evil.test")]
    [DataRow("https://admin.example.com/path")]
    [DataRow("javascript:alert(1)")]
    public void Missing_or_nonexact_origin_is_rejected(string? origin)
    {
        var validator = CreateValidator("https://admin.example.com");

        Assert.IsFalse(validator.IsAllowed(origin, "https://api.example.com"));
    }

    private static AllowedOriginValidator CreateValidator(params string[] allowedOrigins) =>
        new(Options.Create(new IdentityOptions
        {
            AllowDevelopmentEphemeralSigningKey = true,
            AllowedOrigins = allowedOrigins,
        }));
}
