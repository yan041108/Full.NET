using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class MfaRecoveryCodeAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var username = $"mfa-recovery-{Guid.NewGuid():N}";
        await factory.CreateHostIdentityAsync(
            username,
            [],
            cancellationToken,
            password: FullNetApiFactory.TestPassword);
        var accessToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            username,
            FullNetApiFactory.TestPassword,
            cancellationToken);

        using var regenerateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/me/mfa/recovery-codes/regenerate");
        regenerateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var regenerateResponse = await client.SendAsync(regenerateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, regenerateResponse.StatusCode);
        var regenerated = await regenerateResponse.Content.ReadFromJsonAsync<RegenerateMfaRecoveryCodesResponse>(
            cancellationToken);
        Assert.IsNotNull(regenerated);
        Assert.AreEqual(10, regenerated!.RecoveryCodes.Count);
        var code = regenerated.RecoveryCodes[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(code));

        using var consumeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/me/mfa/recovery-codes/consume")
        {
            Content = JsonContent.Create(new ConsumeMfaRecoveryCodeRequest(code)),
        };
        consumeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var consumeResponse = await client.SendAsync(consumeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, consumeResponse.StatusCode);

        using var reuseRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/me/mfa/recovery-codes/consume")
        {
            Content = JsonContent.Create(new ConsumeMfaRecoveryCodeRequest(code)),
        };
        reuseRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var reuseResponse = await client.SendAsync(reuseRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, reuseResponse.StatusCode);
        using var problem = JsonDocument.Parse(await reuseResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(IdentityErrorCodes.MfaRecoveryCodeInvalid, problem.RootElement.GetProperty("code").GetString());
    }
}
