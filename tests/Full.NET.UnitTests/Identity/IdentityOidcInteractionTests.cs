using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Endpoint = Full.NET.Modules.Identity.Features.OidcAuthorization.Endpoint;

namespace Full.NET.UnitTests.Identity;

/// <summary>验证中心登录交互先校验请求来源，再决定是否复用认证。</summary>
[TestClass]
public sealed class IdentityOidcInteractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Invalid_login_antiforgery_is_rejected_before_cookie_or_credentials()
    {
        using var fixture = new Fixture(new OpenIddictRequest { Prompt = "login" });
        fixture.Context.Request.Method = "POST";
        fixture.Context.Request.ContentType = "application/x-www-form-urlencoded";
        fixture.Context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["username"] = "attacker", ["password"] = "password",
        });
        fixture.Antiforgery.ValidateRequestAsync(fixture.Context)
            .Returns(Task.FromException(new AntiforgeryValidationException("invalid")));

        var result = await fixture.RunAsync();

        Assert.AreEqual(400, ((IStatusCodeHttpResult)result).StatusCode);
        await fixture.Authentication.DidNotReceiveWithAnyArgs().SignOutAsync(default!, default, default);
        await fixture.Authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [TestMethod]
    [DataRow(0L, 0L, false)]
    [DataRow(60L, 61L, false)]
    [DataRow(60L, -1L, false)]
    [DataRow(0L, 0L, true)]
    [DataRow(60L, 61L, true)]
    public async Task Max_age_requires_fresh_authentication(long maxAge, long age, bool silent)
    {
        using var fixture = new Fixture(new OpenIddictRequest
        {
            MaxAge = maxAge, Prompt = silent ? "none" : null,
            RedirectUri = "https://client.example/callback", State = "state",
        });
        var identity = new ClaimsIdentity("center");
        if (age >= 0)
            identity.AddClaim(new Claim("auth_time", Now.AddSeconds(-age).ToUnixTimeSeconds().ToString()));
        fixture.Authentication.AuthenticateAsync(fixture.Context, IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
            .Returns(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(identity), IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)));

        var result = await fixture.RunAsync();

        if (silent)
            StringAssert.Contains(((RedirectHttpResult)result).Url, "error=login_required");
        else
            StringAssert.Contains(((ContentHttpResult)result).ResponseContent!, "name=\"username\"");
    }

    [TestMethod]
    [DataRow(60L, 59L, false)]
    [DataRow(60L, 60L, false)]
    [DataRow(60L, 61L, true)]
    [DataRow(0L, 0L, true)]
    [DataRow(60L, -1L, true)]
    public void Authentication_freshness_preserves_valid_cookie(long maxAge, long age, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("auth_time", Now.AddSeconds(-age).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        }));
        Assert.AreEqual(expected, Endpoint.RequiresFreshAuthentication(principal, maxAge, Now));
        Assert.IsFalse(Endpoint.RequiresFreshAuthentication(principal, null, Now));
    }

    [TestMethod]
    public async Task Protocol_exception_uses_static_json_and_does_not_disclose_details()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/connect/token";
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var handler = new Full.NET.Modules.Identity.Http.IdentityOidcProtocolExceptionHandler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Full.NET.Modules.Identity.Http.IdentityOidcProtocolExceptionHandler>.Instance);

        Assert.IsTrue(await handler.TryHandleAsync(context, new InvalidOperationException("secret-value"), default));

        Assert.AreEqual(500, context.Response.StatusCode);
        var json = System.Text.Encoding.UTF8.GetString(body.ToArray());
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual("server_error", document.RootElement.GetProperty("error").GetString());
        Assert.AreEqual("An internal error occurred.", document.RootElement.GetProperty("error_description").GetString());
        Assert.IsFalse(json.Contains("secret-value", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Login_page_contains_encoded_antiforgery_pair()
    {
        using var fixture = new Fixture(new OpenIddictRequest());
        var result = await fixture.RunAsync();
        StringAssert.Contains(((ContentHttpResult)result).ResponseContent!, "name=\"__RequestVerificationToken\" value=\"form-token\"");
        fixture.Antiforgery.Received(1).GetAndStoreTokens(fixture.Context);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public Fixture(OpenIddictRequest request)
        {
            _services = new ServiceCollection().AddSingleton(Authentication).BuildServiceProvider();
            Context.RequestServices = _services;
            Context.Request.Method = "GET";
            Context.Features.Set(new OpenIddictServerAspNetCoreFeature
            {
                Transaction = new OpenIddictServerTransaction { Request = request },
            });
            Authentication.AuthenticateAsync(Context, IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
                .Returns(AuthenticateResult.NoResult());
            Antiforgery.GetAndStoreTokens(Context)
                .Returns(new AntiforgeryTokenSet("form-token", "cookie-token", "__RequestVerificationToken", null));
        }
        public DefaultHttpContext Context { get; } = new();
        public IAuthenticationService Authentication { get; } = Substitute.For<IAuthenticationService>();
        public IAntiforgery Antiforgery { get; } = Substitute.For<IAntiforgery>();
        public Task<IResult> RunAsync() => Endpoint.HandleAuthorizeAsync(
            Context, null!, null!, Options.Create(new IdentityOidcOptions()), Antiforgery, new FixedClock(), default);
        public void Dispose() => _services.Dispose();
    }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }
}
