using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class WebhookDeliveryAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantWithPermissionsAsync(
            factory,
            client,
            [
                WebhookPermissions.SubscriptionsRead,
                WebhookPermissions.SubscriptionsManage,
            ],
            cancellationToken);
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/webhooks/subscriptions");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var items = await listResponse.Content.ReadFromJsonAsync<WebhookSubscriptionResponse[]>(
            cancellationToken);
        Assert.IsNotNull(items);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/subscriptions")
        {
            Content = JsonContent.Create(new CreateWebhookSubscriptionRequest(
                WorkflowNotificationIntegrationEventTypes.InstanceCompleted,
                "https://example.com/fullnet-webhook-test",
                "integration-test-secret")),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.IsTrue(created!.IsActive);
    }

    private static async Task<string> LoginAndEnterAcmeTenantWithPermissionsAsync(
        FullNetApiFactory factory,
        HttpClient client,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        var identity = await factory.CreateHostIdentityAsync(
            $"webhook-{Guid.NewGuid():N}",
            permissions,
            cancellationToken);
        using var availableRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            identity.AccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content.ReadFromJsonAsync<TenantContextSummary[]>(
            cancellationToken);
        var acme = available!.Single(tenant => tenant.Identifier == "acme");

        using var enterRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(acme.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            identity.AccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        return identity.AccessToken;
    }
}
