using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Full.NET.UnitTests.Identity;

/// <summary>手动刷新签发必须与标准服务端管线保持相同 issuer，并使用轮换后的当前选项。</summary>
[TestClass]
public sealed class IdentityOidcContextRefreshTokenIssuerTests
{
    [TestMethod]
    public async Task Context_refresh_uses_private_issuer_and_live_options_after_rotation()
    {
        var first = new OpenIddictServerOptions { Issuer = new Uri("https://id.example/identity"), RefreshTokenLifetime = TimeSpan.FromDays(2) };
        var second = new OpenIddictServerOptions { Issuer = first.Issuer, RefreshTokenLifetime = TimeSpan.FromDays(4) };
        var monitor = Substitute.For<IOptionsMonitor<OpenIddictServerOptions>>(); monitor.CurrentValue.Returns(first);
        var dispatcher = Substitute.For<IOpenIddictServerDispatcher>();
        var contexts = new List<OpenIddictServerEvents.GenerateTokenContext>();
        dispatcher.When(value => value.DispatchAsync(Arg.Any<OpenIddictServerEvents.GenerateTokenContext>())).Do(call =>
        {
            var context = call.Arg<OpenIddictServerEvents.GenerateTokenContext>();
            Assert.IsNotNull(context); contexts.Add(context); context.Token = "opaque-test-token";
        });
        var clock = new TestClock();
        var issuer = new IdentityOidcContextRefreshTokenIssuer(dispatcher, monitor,
            Substitute.For<ILogger<IdentityOidcContextRefreshTokenIssuer>>(), clock);
        var source = new ClaimsPrincipal(new ClaimsIdentity()); source.SetAuthorizationId("authorization-test");
        var request = new IdentityOidcContextAccessTokenIssueRequest(Guid.CreateVersion7(), "name", "user", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "client", "host", "host", null, ["openid", "offline_access"], [], false, "stamp", false, "resource");
        Assert.AreEqual("opaque-test-token", await issuer.TryIssueAsync(source, request));
        monitor.CurrentValue.Returns(second);
        Assert.AreEqual("opaque-test-token", await issuer.TryIssueAsync(source, request));
        Assert.AreSame(first, contexts[0].Options); Assert.AreSame(second, contexts[1].Options);
        Assert.AreEqual(first.Issuer.AbsoluteUri, contexts[0].Principal.GetClaim(OpenIddictConstants.Claims.Private.Issuer));
        Assert.AreEqual("authorization-test", contexts[1].Principal.GetAuthorizationId());
        Assert.AreEqual(clock.UtcNow.AddDays(4), contexts[1].Principal.GetExpirationDate());
    }

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; } = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero); }
}
