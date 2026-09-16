using System.Net;
using System.Net.Http.Headers;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcProtocolLifecycleAssertions
{
    internal sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var clock = new MutableClock();
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:AccessTokenMinutes"] = "1",
        };
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            settings,
            configureTestServices: services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            });
        await factory.InitializeAsync(cancellationToken);
        await VerifyRefreshExtendsExpiredApplicationSessionAsync(factory, clock, cancellationToken);
    }

    private static async Task VerifyRefreshExtendsExpiredApplicationSessionAsync(
        FullNetApiFactory factory,
        MutableClock clock,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        var initialBinding = ReadOidcAccessTokenBinding(flow.AccessToken);

        clock.UtcNow = clock.UtcNow.AddMinutes(2);

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsTrue(
            refreshResult.IsSuccessStatusCode,
            $"Refresh must succeed after sliding application session extension, got: {refreshResult.RawBody}");

        var refreshedAccessToken = ReadAccessToken(refreshResult.RawBody);
        var refreshedBinding = ReadOidcAccessTokenBinding(refreshedAccessToken);
        Assert.AreEqual(initialBinding.ApplicationSessionId, refreshedBinding.ApplicationSessionId);
        Assert.AreEqual(initialBinding.CenterSessionId, refreshedBinding.CenterSessionId);

        using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedAccessToken);
        using var userInfoResponse = await client.SendAsync(userInfoRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, userInfoResponse.StatusCode);
    }

    private static string ReadAccessToken(string rawTokenResponse)
    {
        using var document = System.Text.Json.JsonDocument.Parse(rawTokenResponse);
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Refresh response missing access_token.");
    }

    private static OidcAccessTokenBinding ReadOidcAccessTokenBinding(string accessToken)
    {
        var applicationSessionId = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.ApplicationSessionId);
        var centerSessionId = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.CenterSessionId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(applicationSessionId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(centerSessionId));
        return new OidcAccessTokenBinding(
            Guid.Parse(applicationSessionId!),
            Guid.Parse(centerSessionId!));
    }

    private sealed record OidcAccessTokenBinding(Guid ApplicationSessionId, Guid CenterSessionId);
}
