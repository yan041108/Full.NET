using Full.NET.Abstractions.Results;
using Full.NET.Hosting.RateLimiting;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class RateLimitPolicyErrorCodesTests
{
    [TestMethod]
    public void Resolve_returns_mapped_policy_code_or_hosting_fallback()
    {
        var registry = new RateLimitPolicyErrorCodes();
        registry.MapPolicy("identity-login", "identity.authentication.rate_limited");

        Assert.AreEqual(
            "identity.authentication.rate_limited",
            registry.Resolve("identity-login", CommonErrorCodes.RateLimited));
        Assert.AreEqual(
            CommonErrorCodes.RateLimited,
            registry.Resolve("unknown-policy", CommonErrorCodes.RateLimited));
        Assert.AreEqual(
            CommonErrorCodes.RateLimited,
            registry.Resolve(null, CommonErrorCodes.RateLimited));
    }
}
