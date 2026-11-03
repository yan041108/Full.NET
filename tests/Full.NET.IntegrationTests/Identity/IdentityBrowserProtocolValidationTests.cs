using System.Net;
using System.Text.Json.Nodes;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>保持有效签名，仅改变协议约束，证明 RP 自己拒绝错误身份令牌。</summary>
[TestClass]
public sealed class IdentityBrowserProtocolValidationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Browser_callback_rejects_signed_tokens_with_wrong_nonce_issuer_audience_or_expiration(DatabaseProvider provider)
    {
        var transport = new IdentityBrowserTestTransport();
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider),
            IdentityBrowserTestSupport.Settings(), configureTestServices: services => services.AddSingleton<IHttpMessageHandlerBuilderFilter>(transport));
        transport.Factory = factory; await factory.InitializeAsync();
        var keys = factory.Services.GetRequiredService<IdentityOidcSigningKeyRing>();
        foreach (var claim in new[] { "nonce", "iss", "aud", "exp" })
        {
            transport.Transform = async (request, response) =>
            {
                if (request.RequestUri!.AbsolutePath != "/connect/token" || !response.IsSuccessStatusCode) return response;
                var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
                var identity = body["id_token"]!.GetValue<string>();
                var payload = JsonNode.Parse(WebEncoders.Base64UrlDecode(identity.Split('.')[1]))!.AsObject();
                payload[claim] = claim == "exp" ? JsonValue.Create(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()) : JsonValue.Create("invalid-" + claim);
                if (claim == "exp") { payload["iat"] = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(); payload["nbf"] = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(); }
                body["id_token"] = new JsonWebTokenHandler().CreateToken(payload.ToJsonString(), keys.SigningCredentials);
                response.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
                return response;
            };
            using var browser = IdentityBrowserTestSupport.Browser(factory);
            var callback = await IdentityBrowserTestSupport.BeginBrowserLoginAsync(browser);
            using var rejected = await browser.GetAsync(callback);
            Assert.IsTrue(rejected.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized, claim);
            Assert.IsFalse(rejected.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(value => value.StartsWith(IdentityBrowserSession.CookieName + "=", StringComparison.Ordinal)), claim);
            using var absent = await browser.GetAsync("/api/v1/me"); Assert.AreEqual(HttpStatusCode.Unauthorized, absent.StatusCode, claim);
        }
    }
}
