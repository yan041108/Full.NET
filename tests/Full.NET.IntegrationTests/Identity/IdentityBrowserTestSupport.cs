using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>只替换 TestServer 传输，不伪造 Discovery、JWKS、授权码、回调或令牌校验结果。</summary>
internal sealed class IdentityBrowserTestTransport : IHttpMessageHandlerBuilderFilter
{
    internal FullNetApiFactory Factory { get; set; } = null!;
    internal int TokenRequests;
    internal Func<HttpRequestMessage, HttpResponseMessage, Task<HttpResponseMessage>>? Transform;
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name?.Contains("OpenIddict", StringComparison.OrdinalIgnoreCase) == true)
        {
            builder.PrimaryHandler.Dispose();
            builder.PrimaryHandler = new Forwarder(this);
        }
    };
    private sealed class Forwarder(IdentityBrowserTestTransport owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.AbsolutePath == "/connect/token") Interlocked.Increment(ref owner.TokenRequests);
            using var invoker = new HttpMessageInvoker(owner.Factory.Server.CreateHandler());
            var response = await invoker.SendAsync(request, token);
            return owner.Transform is null ? response : await owner.Transform(request, response);
        }
    }
}

internal static class IdentityBrowserTestSupport
{
    internal static Dictionary<string, string?> Settings()
    {
        using var rsa = RSA.Create(2048);
        return new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:ActiveSigningKeyId"] = "browser-test",
            ["Identity:Oidc:SigningKeys:browser-test:PublicKeyPem"] = rsa.ExportRSAPublicKeyPem(),
            ["Identity:Oidc:SigningKeys:browser-test:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
            ["Identity:Oidc:EncryptionKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Identity:Browser:Enable"] = "true", ["Identity:Browser:PublicOrigin"] = "https://localhost",
            ["Identity:Browser:ClientId"] = "fullnet-admin-bff", ["Identity:Browser:ClientSecret"] = "browser-fixture-confidential-secret",
            ["Identity:Browser:StateSigningKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Identity:Browser:StateEncryptionKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "oidc-browser-dp-" + Guid.NewGuid().ToString("N"))
        };
    }
    internal static async Task<string> DatabaseAsync(DatabaseProvider provider) => provider == DatabaseProvider.SqlServer
        ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
    internal static HttpClient Browser(FullNetApiFactory factory)
    {
        var browser = factory.CreateClientForHost("localhost"); browser.BaseAddress = new Uri("https://localhost"); return browser;
    }
    internal static async Task<Uri> BeginBrowserLoginAsync(HttpClient browser, string password = FullNetApiFactory.TestPassword)
    {
        using var start = await browser.GetAsync("/api/v1/identity/browser/login");
        Assert.AreEqual(HttpStatusCode.Redirect, start.StatusCode);
        using var page = await browser.GetAsync(start.Headers.Location);
        if (page.StatusCode == HttpStatusCode.Redirect) return page.Headers.Location!;
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        var fields = Hidden(await page.Content.ReadAsStringAsync());
        fields["username"] = "admin"; fields["password"] = password;
        using var submitted = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, submitted.StatusCode);
        return submitted.Headers.Location!;
    }
    internal static async Task LoginBrowserAsync(HttpClient browser)
    {
        using var callback = await browser.GetAsync(await BeginBrowserLoginAsync(browser));
        Assert.AreEqual(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.AreEqual("/#/identity/oidc/callback", callback.Headers.Location!.OriginalString);
    }
    internal static Dictionary<string, string> Hidden(string html) => Regex.Matches(html,
        "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")
        .ToDictionary(match => WebUtility.HtmlDecode(match.Groups[1].Value), match => WebUtility.HtmlDecode(match.Groups[2].Value));
}
