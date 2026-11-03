using System.Security.Cryptography;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Middleware;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Identity;

/// <summary>配置与请求边界只接受精确同源、独立持久化状态材料和部署许可目的地。</summary>
[TestClass]
public sealed class IdentityBrowserBoundaryTests
{
    [TestMethod]
    public void Browser_configuration_rejects_shared_keys_duplicate_registration_and_non_origin_urls()
    {
        var browser = new IdentityBrowserOptions { Enable = true, PublicOrigin = "https://admin.example", ClientSecret = "secret",
            StateSigningKeyBase64 = Key(), StateEncryptionKeyBase64 = Key() };
        var oidc = new IdentityOidcOptions { Enable = true, EncryptionKeyBase64 = Key() };
        IdentityBrowserOptionsValidation.Validate(browser, oidc);
        browser.PublicOrigin += "/nested";
        Assert.ThrowsExactly<InvalidOperationException>(() => IdentityBrowserOptionsValidation.Validate(browser, oidc));
        browser.PublicOrigin = "https://admin.example"; browser.StateEncryptionKeyBase64 = oidc.EncryptionKeyBase64;
        Assert.ThrowsExactly<InvalidOperationException>(() => IdentityBrowserOptionsValidation.Validate(browser, oidc));
        browser.StateEncryptionKeyBase64 = " " + browser.StateSigningKeyBase64 + "\n";
        Assert.ThrowsExactly<InvalidOperationException>(() => IdentityBrowserOptionsValidation.Validate(browser, oidc));
        browser.StateEncryptionKeyBase64 = Key(); oidc.Clients = [new() { ClientId = browser.ClientId }];
        Assert.ThrowsExactly<InvalidOperationException>(() => IdentityBrowserOptionsValidation.Validate(browser, oidc));
    }

    [TestMethod]
    public void Unsafe_cookie_requests_require_exact_origin_or_same_origin_referer()
    {
        var context = new DefaultHttpContext();
        Assert.IsFalse(IdentityBrowserMiddleware.IsSameOrigin(context, "https://admin.example"));
        context.Request.Headers.Referer = "https://admin.example/settings";
        Assert.IsTrue(IdentityBrowserMiddleware.IsSameOrigin(context, "https://admin.example"));
        context.Request.Headers.Origin = "https://sibling.example";
        Assert.IsFalse(IdentityBrowserMiddleware.IsSameOrigin(context, "https://admin.example"));
        context.Request.Headers.Origin = "https://admin.example.evil.test";
        Assert.IsFalse(IdentityBrowserMiddleware.IsSameOrigin(context, "https://admin.example"));
    }

    [TestMethod]
    public void Backchannel_destinations_cannot_expand_the_deployment_network_allowlist()
    {
        string[] allowed = ["https://rp.example"];
        Assert.IsTrue(IdentityOidcBackchannelPolicy.IsAllowed("https://rp.example/logout", allowed));
        foreach (var destination in new[] { "http://rp.example/logout", "https://rp.example.evil/logout", "https://user@rp.example/logout", "https://rp.example/logout?secret=value", "https://127.0.0.1/logout" })
            Assert.IsFalse(IdentityOidcBackchannelPolicy.IsAllowed(destination, allowed), destination);
    }

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
