using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.DataApproval.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class DataApprovalApiAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyScenariosRequireAuthAsync(client, cancellationToken);
        await VerifyScenarioCatalogAsync(factory, client, cancellationToken);
        await VerifyUnknownScenarioReturnsNotFoundAsync(factory, client, cancellationToken);
    }

    private static async Task VerifyScenariosRequireAuthAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/api/v1/data-approvals/scenarios",
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task VerifyScenarioCatalogAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var token = await factory.CreateHostAccessTokenAsync(
            [DataApprovalPermissions.ScenariosRead],
            cancellationToken);
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/data-approvals/scenarios");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var scenarios = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<DataApprovalScenarioResponse>>(
            cancellationToken);
        Assert.IsNotNull(scenarios);
        Assert.IsTrue(scenarios!.Count >= 2);
        Assert.IsTrue(scenarios.Any(item =>
            item.ScenarioKey == DataApprovalScenarioKeys.SerialRuleHostUpdate));
        Assert.IsTrue(scenarios.Any(item =>
            item.ScenarioKey == DataApprovalScenarioKeys.SerialRuleHostDisable));

        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/data-approvals/scenarios/{DataApprovalScenarioKeys.SerialRuleHostUpdate}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        var detail = await getResponse.Content.ReadFromJsonAsync<DataApprovalScenarioResponse>(cancellationToken);
        Assert.IsNotNull(detail);
        Assert.AreEqual(DataApprovalScenarioKeys.SerialRuleHostUpdate, detail!.ScenarioKey);
    }

    private static async Task VerifyUnknownScenarioReturnsNotFoundAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var token = await factory.CreateHostAccessTokenAsync(
            [DataApprovalPermissions.ScenariosRead],
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/data-approvals/scenarios/not-a-registered-scenario");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
