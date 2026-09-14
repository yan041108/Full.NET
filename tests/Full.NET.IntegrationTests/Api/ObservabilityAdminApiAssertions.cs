using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Caching.Fusion;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;

namespace Full.NET.IntegrationTests.Api;

internal static class ObservabilityAdminApiAssertions
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using (var anonymous = await client.GetAsync(
                   "/api/v1/observability/log-files",
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var readToken = await factory.CreateHostAccessTokenAsync(
            ["observability.log_files.read"],
            cancellationToken);
        using var listRequest = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/observability/log-files",
            readToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var files = await listResponse.Content.ReadFromJsonAsync<LogFileSummary[]>(
            cancellationToken);
        Assert.IsNotNull(files);
        Assert.HasCount(1, files);
        Assert.AreEqual("api.log", files[0].FileName);
        Assert.AreEqual(64, files[0].Id.Length);

        using var tailRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/api/v1/observability/log-files/{files[0].Id}/tail?maximumLines=2&maximumBytes=64",
            readToken);
        using var tailResponse = await client.SendAsync(tailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, tailResponse.StatusCode);
        var tail = await tailResponse.Content.ReadFromJsonAsync<LogFileTail>(
            cancellationToken);
        Assert.IsNotNull(tail);
        Assert.AreEqual("second\nthird", tail.Content);

        using var deniedDownloadRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/api/v1/observability/log-files/{files[0].Id}/download",
            readToken);
        using var deniedDownload = await client.SendAsync(
            deniedDownloadRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedDownload.StatusCode);

        var downloadToken = await factory.CreateHostAccessTokenAsync(
            ["observability.log_files.download"],
            cancellationToken);
        using var downloadRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/api/v1/observability/log-files/{files[0].Id}/download",
            downloadToken);
        using var downloadResponse = await client.SendAsync(
            downloadRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.AreEqual("first\nsecond\nthird\n", await downloadResponse.Content.ReadAsStringAsync(cancellationToken));

        using (var deniedRuntime = await client.GetAsync(
                   "/api/v1/observability/server-instances/current/runtime",
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, deniedRuntime.StatusCode);
        }

        var serverToken = await factory.CreateHostAccessTokenAsync(
            ["observability.server.read"],
            cancellationToken);
        using var instancesRequest = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/observability/server-instances",
            serverToken);
        using var instancesResponse = await client.SendAsync(instancesRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, instancesResponse.StatusCode);
        var instances = await instancesResponse.Content.ReadFromJsonAsync<ServerInstanceCatalogEntry[]>(
            WebJson,
            cancellationToken);
        Assert.IsNotNull(instances);
        Assert.IsGreaterThan(0, instances!.Length);
        var current = instances.Single(entry => entry.IsCurrent);
        Assert.AreEqual(ServerInstanceRuntimeQueryability.Local, current.RuntimeQueryability);

        using var runtimeRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/api/v1/observability/server-instances/{current.InstanceKey}/runtime",
            serverToken);
        using var runtimeResponse = await client.SendAsync(runtimeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, runtimeResponse.StatusCode);
        var runtime = await runtimeResponse.Content.ReadFromJsonAsync<ServerRuntimeSnapshot>(
            cancellationToken);
        Assert.IsNotNull(runtime);
        Assert.AreEqual(current.InstanceKey, runtime.InstanceKey);
        Assert.IsFalse(string.IsNullOrWhiteSpace(runtime.FrameworkDescription));
        var payload = await runtimeResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("ConnectionString", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", payload, StringComparison.OrdinalIgnoreCase);

        using var remoteRuntimeRequest = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/observability/server-instances/unknown-instance/runtime",
            serverToken);
        using var remoteRuntimeResponse = await client.SendAsync(
            remoteRuntimeRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, remoteRuntimeResponse.StatusCode);

        using (var deniedPolicies = await client.GetAsync(
                   "/api/v1/observability/cache-policies",
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, deniedPolicies.StatusCode);
        }

        var cacheReadToken = await factory.CreateHostAccessTokenAsync(
            ["observability.cache_policies.read"],
            cancellationToken);
        using var policiesRequest = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/observability/cache-policies",
            cacheReadToken);
        using var policiesResponse = await client.SendAsync(policiesRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, policiesResponse.StatusCode);
        var policies = await policiesResponse.Content.ReadFromJsonAsync<CachePolicySummary[]>(
            cancellationToken);
        Assert.IsNotNull(policies);
        Assert.IsGreaterThan(0, policies.Length);
        var tenantPolicy = policies.Single(policy => policy.EntryName == CacheEntryNames.TenantResolution);
        Assert.IsTrue(tenantPolicy.CanInvalidate);
        var policyPayload = await policiesResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("RedisConnectionString", policyPayload, StringComparison.OrdinalIgnoreCase);

        using var deniedInvalidateRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/observability/cache-policies/{tenantPolicy.EntryName}/invalidations",
            cacheReadToken,
            new CacheInvalidationRequest(
                "by-tenant",
                new Dictionary<string, string>
                {
                    ["tenantId"] = Guid.Empty.ToString(),
                    ["domain"] = "invalid",
                },
                null));
        using var deniedInvalidateResponse = await client.SendAsync(
            deniedInvalidateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedInvalidateResponse.StatusCode);

        var cacheInvalidateToken = await factory.CreateHostAccessTokenAsync(
            ["observability.cache_policies.invalidate"],
            cancellationToken);
        using var invalidInvalidateRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/observability/cache-policies/{tenantPolicy.EntryName}/invalidations",
            cacheInvalidateToken,
            new CacheInvalidationRequest(
                "by-tenant",
                new Dictionary<string, string>
                {
                    ["tenantId"] = Guid.Empty.ToString(),
                    ["domain"] = "invalid",
                },
                null));
        using var invalidInvalidateResponse = await client.SendAsync(
            invalidInvalidateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidInvalidateResponse.StatusCode);
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
