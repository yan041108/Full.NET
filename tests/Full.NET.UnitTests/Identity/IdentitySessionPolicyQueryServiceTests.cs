using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentitySessionPolicyQueryServiceTests
{
    [TestMethod]
    public void Returns_configured_login_policy()
    {
        var service = new IdentitySessionPolicyQueryService(
            Options.Create(new IdentityOptions
            {
                SessionLoginPolicy = IdentitySessionLoginPolicy.SingleSession,
            }));

        var result = service.GetPolicy();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            IdentitySessionLoginPolicy.SingleSession,
            result.Value!.LoginPolicy);
    }
}
