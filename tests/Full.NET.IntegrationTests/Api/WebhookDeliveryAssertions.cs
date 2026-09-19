using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
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
        using var client = factory.CreateClientForHost("acme.localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            ["webhooks.subscriptions.read", "webhooks.subscriptions.manage"],
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
}