using System.Net;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class AccountLifecycleAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        var deliveryPort = new CapturingIdentityChallengeDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(
            factory.Provider,
            factory.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<IIdentityChallengeDeliveryPort>(deliveryPort);
            });
        await scopedFactory.InitializeAsync(cancellationToken);
        using var client = scopedFactory.CreateClientForHost("localhost");

        var email = $"recover-{Guid.NewGuid():N}@example.com";
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        await CreateHostUserWithEmailAsync(client, adminToken, email, cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/recover-password")
        {
            Content = JsonContent.Create(new RequestPasswordRecoveryRequest(email)),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(deliveryPort.LastIntent);
        Assert.AreEqual(email, deliveryPort.LastIntent!.NormalizedEmail);

        var confirmRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/recover-password/confirm")
        {
            Content = JsonContent.Create(new ConfirmPasswordRecoveryRequest(
                deliveryPort.LastIntent.ChallengeId,
                deliveryPort.LastIntent.Credential,
                "FullNet!2026Recovered")),
        };
        using var confirmResponse = await client.SendAsync(confirmRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, confirmResponse.StatusCode);
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("admin", FullNetApiFactory.TestPassword),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return token!.AccessToken;
    }

    private static async Task CreateHostUserWithEmailAsync(
        HttpClient client,
        string adminToken,
        string email,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/users")
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken) },
            Content = JsonContent.Create(new CreateHostUserRequest(
                $"user-{Guid.NewGuid():N}",
                "Recovery Target",
                FullNetApiFactory.TestPassword,
                Profile: new HostUserProfileWriteRequest(
                    FieldKeys: ["email", "nickname"],
                    Nickname: "Recovery Target",
                    PhoneNumber: null,
                    Email: email,
                    EmployeeNumber: null,
                    Gender: null,
                    JoinDateUtc: null,
                    SortOrder: null,
                    IdCardType: null,
                    IdCardNumber: null,
                    BirthDate: null,
                    Ethnicity: null,
                    Address: null,
                    GraduatedSchool: null,
                    EducationLevel: null,
                    PoliticalStatus: null,
                    OfficePhone: null,
                    EmergencyContact: null,
                    EmergencyContactRelation: null,
                    EmergencyContactPhone: null,
                    EmergencyContactAddress: null,
                    Remark: null,
                    Version: null))),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
