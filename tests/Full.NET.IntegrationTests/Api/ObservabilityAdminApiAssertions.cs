using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;

namespace Full.NET.IntegrationTests.Api;

internal static class ObservabilityAdminApiAssertions
{
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
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
