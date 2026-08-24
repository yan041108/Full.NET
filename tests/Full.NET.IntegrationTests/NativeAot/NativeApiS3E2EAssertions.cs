using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native Host.Api 通过真实 AWSSDK.S3 访问 MinIO 的 HTTP 文件链路断言。
/// </summary>
internal static class NativeApiS3E2EAssertions
{
    public static async Task VerifyS3HttpFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
            provider,
            connectionString,
            cancellationToken).ConfigureAwait(false);

        await using var minio = await MinioTestEnvironment.StartAsync().ConfigureAwait(false);
        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            minio.CreateNativeHostSettings(),
            TimeSpan.FromMinutes(3),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(
            client,
            host.LogFilePath,
            cancellationToken)
            .ConfigureAwait(false);

        var payload = Encoding.UTF8.GetBytes($"native-s3-{Guid.NewGuid():N}");
        var fileName = "native-s3.txt";

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", fileName);

        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/files/host-files")
        {
            Content = uploadContent,
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var uploadResponse = await client.SendAsync(uploadRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, uploadResponse.StatusCode);

        var created = await uploadResponse.Content.ReadFromJsonAsync<HostFileResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(created);
        Assert.AreEqual(fileName, created.OriginalFileName);
        Assert.AreEqual(payload.Length, created.SizeBytes);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            created.ContentHash);

        var storageKey = string.Create(
            CultureInfo.InvariantCulture,
            $"host/{created.CreatedAtUtc:yyyy}/{created.CreatedAtUtc:MM}/{created.Id:N}");
        Assert.IsTrue(
            await minio.ObjectExistsAsync(storageKey).ConfigureAwait(false),
            $"S3 对象应存在：{storageKey}");

        using var downloadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files/{created.Id:D}/content");
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var downloadResponse = await client.SendAsync(downloadRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloaded = await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        CollectionAssert.AreEqual(payload, downloaded);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/files/host-files?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        using var listPayload = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.IsTrue(
            listPayload.RootElement.GetProperty("items").EnumerateArray()
                .Any(item => item.GetProperty("id").GetGuid() == created.Id));

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/files/host-files/{created.Id:D}/delete")
        {
            Content = JsonContent.Create(new { }),
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var missingDownloadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files/{created.Id:D}/content");
        missingDownloadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var missingDownloadResponse = await client.SendAsync(
            missingDownloadRequest,
            cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NotFound, missingDownloadResponse.StatusCode);

        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }
}
