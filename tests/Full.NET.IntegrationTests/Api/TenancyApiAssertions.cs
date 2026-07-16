using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class TenancyApiAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);

        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        using var response = await tenantClient
            .GetAsync("/api/v1/tenancy/current", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var successJson = await response.Content
            .ReadAsStringAsync(cancellationToken);
        var tenant = JsonSerializer.Deserialize<TenantSummary>(
            successJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(tenant);
        Assert.AreEqual("acme", tenant.Identifier);
        Assert.AreEqual("acme.localhost", tenant.Domain);
        using (var successDocument = JsonDocument.Parse(successJson))
        {
            Assert.IsFalse(successDocument.RootElement.TryGetProperty("success", out _));
            Assert.IsFalse(successDocument.RootElement.TryGetProperty("code", out _));
            Assert.IsFalse(successDocument.RootElement.TryGetProperty("data", out _));
        }

        using var missingClient = factory.CreateClientForHost("missing.localhost");
        using var missingResponse = await missingClient
            .GetAsync("/api/v1/tenancy/current", cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "tenancy.host-not-found",
            problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));
    }
}
