using System.Net;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>协议入口预算按可信来源隔离；伪造客户端和转发标头不能增加预算。</summary>
[TestClass]
public sealed class IdentityOidcProtocolRateLimitTests
{
    [TestMethod]
    [DataRow(0, true)]
    [DataRow(-1, true)]
    [DataRow(10001, true)]
    [DataRow(120, false)]
    public void Enabled_protocol_validates_its_own_budget(int permits, bool rejected)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var result = new IdentityOidcOptionsValidator(environment).Validate(null,
            new IdentityOidcOptions { Enable = true, Issuer = "http://localhost", AllowDevelopmentEphemeralSigningKey = true,
                ProtocolRateLimitPermitLimitPerMinute = permits });
        Assert.AreEqual(rejected, result.Failures?.Any(f => f.Contains(nameof(IdentityOidcOptions.ProtocolRateLimitPermitLimitPerMinute), StringComparison.Ordinal)) == true);
    }

    [TestMethod]
    public void Exhausted_source_cannot_change_request_fields_to_get_more_budget()
    {
        using var limiter = new IdentityOidcProtocolRequestLimiter(Options.Create(new IdentityOidcOptions { ProtocolRateLimitPermitLimitPerMinute = 1 }));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.IsTrue(limiter.TryAcquire(context));
        context.Request.Headers["X-Forwarded-For"] = "192.0.2.2";
        context.Request.QueryString = new QueryString("?client_id=other-client");
        Assert.IsFalse(limiter.TryAcquire(context));
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.3");
        Assert.IsTrue(limiter.TryAcquire(context));
    }
}
