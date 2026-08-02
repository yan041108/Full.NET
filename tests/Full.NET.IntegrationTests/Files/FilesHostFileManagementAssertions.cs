using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Cleanup;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Files;

/// <summary>Host 文件元数据纵向切片验收夹具。</summary>
internal static class FilesHostFileManagementAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyPendingUploadReconciliationAsync(factory, cancellationToken);
        await VerifyUploadDownloadAndDeleteAsync(
            factory,
            client,
            cancellationToken);
        await VerifyExactHostFileActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
        await OpenApiFilesHostFilesContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifyPendingUploadReconciliationAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var providers = scope.ServiceProvider.GetRequiredService<FileStorageProviderRegistry>();
            var storage = providers.DefaultProvider;
            var createdAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
            var createdByUserId = Guid.CreateVersion7();
            var existingId = Guid.CreateVersion7();
            var missingId = Guid.CreateVersion7();
            var publishingId = Guid.CreateVersion7();
            var existingKey = $"host/reconciliation/{existingId:N}";
            var missingKey = $"host/reconciliation/{missingId:N}";
            var publishingKey = $"host/reconciliation/{publishingId:N}";

            foreach (var candidate in new[]
                     {
                         (Id: existingId, Key: existingKey, Name: "existing.bin"),
                         (Id: missingId, Key: missingKey, Name: "missing.bin"),
                         (Id: publishingId, Key: publishingKey, Name: "publishing.bin"),
                     })
            {
                var affected = await command.ExecuteAsync(
                    HostFileSql.Insert,
                    new
                    {
                        candidate.Id,
                        OriginalFileName = candidate.Name,
                        ContentType = "application/octet-stream",
                        SizeBytes = 1L,
                        storage.ProviderKey,
                        StorageKey = candidate.Key,
                        ContentHash = (string?)null,
                        CreatedAtUtc = createdAtUtc,
                        CreatedByUserId = createdByUserId,
                    },
                    cancellationToken);
                Assert.AreEqual(1, affected);
            }

            Assert.AreEqual(
                1,
                await command.ExecuteAsync(
                    HostFileSql.ClaimPublication,
                    new
                    {
                        FileId = publishingId,
                        storage.ProviderKey,
                        StorageKey = publishingKey,
                    },
                    cancellationToken));

            await using (var content = new MemoryStream([42], writable: false))
            {
                await storage.SaveAsync(existingKey, content, cancellationToken);
            }

            Assert.IsNull(await query.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new { FileId = existingId },
                cancellationToken));

            var runner = ActivatorUtilities.CreateInstance<
                PendingHostFileReconciliationRunner>(scope.ServiceProvider);
            var result = await runner.RunOnceAsync(
                new PendingHostFileReconciliationOptions
                {
                    Enabled = true,
                    BatchSize = 50,
                    MaxBatchesPerRun = 10,
                    MinimumAgeSeconds = 30,
                },
                cancellationToken);

            Assert.IsTrue(result.Promoted >= 1);
            Assert.IsTrue(result.Purged >= 1);
            Assert.IsTrue(result.RetainedPublishing >= 1);
            Assert.IsNotNull(await query.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new { FileId = existingId },
                cancellationToken));
            Assert.AreEqual(
                0L,
                await query.QuerySingleOrDefaultAsync<long>(
                    new SqlStatement(
                        "test.files.count-purged-pending",
                        """
                        SELECT COUNT(1)
                        FROM fn_files_file
                        WHERE Id = @FileId
                          AND TenantId IS NULL
                        """,
                        SqlDataScope.HostOnly),
                    new { FileId = missingId },
                    cancellationToken));
            Assert.AreEqual(
                "publishing",
                await query.QuerySingleOrDefaultAsync<string>(
                    new SqlStatement(
                        "test.files.read-retained-publishing-state",
                        """
                        SELECT StorageState
                        FROM fn_files_file
                        WHERE Id = @FileId
                          AND TenantId IS NULL
                        """,
                        SqlDataScope.HostOnly),
                    new { FileId = publishingId },
                    cancellationToken));

            await storage.DeleteAsync(existingKey, CancellationToken.None);
            Assert.AreEqual(
                1,
                await command.ExecuteAsync(
                    new SqlStatement(
                        "test.files.purge-reconciled-file",
                        """
                        DELETE FROM fn_files_file
                        WHERE Id = @FileId
                          AND TenantId IS NULL
                          AND ProviderKey = @ProviderKey
                          AND StorageKey = @StorageKey
                        """,
                        SqlDataScope.HostOnly),
                    new
                    {
                        FileId = existingId,
                        storage.ProviderKey,
                        StorageKey = existingKey,
                    },
                    CancellationToken.None));
            Assert.AreEqual(
                1,
                await command.ExecuteAsync(
                    new SqlStatement(
                        "test.files.purge-retained-publishing-file",
                        """
                        DELETE FROM fn_files_file
                        WHERE Id = @FileId
                          AND TenantId IS NULL
                          AND ProviderKey = @ProviderKey
                          AND StorageKey = @StorageKey
                        """,
                        SqlDataScope.HostOnly),
                    new
                    {
                        FileId = publishingId,
                        storage.ProviderKey,
                        StorageKey = publishingKey,
                    },
                    CancellationToken.None));
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/files/host-files?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyUploadDownloadAndDeleteAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var payload = Encoding.UTF8.GetBytes($"files-integration-{Guid.NewGuid():N}");
        var fileName = "integration.txt";

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
            adminToken);
        using var uploadResponse = await client.SendAsync(uploadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, uploadResponse.StatusCode);
        var created = await uploadResponse.Content.ReadFromJsonAsync<HostFileResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(fileName, created.OriginalFileName);
        Assert.AreEqual(payload.Length, created.SizeBytes);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            created.ContentHash);

        using var downloadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files/{created.Id:D}/content");
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var downloadResponse = await client.SendAsync(downloadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloaded = await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        CollectionAssert.AreEqual(payload, downloaded);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/files/host-files?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedHostFileResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));

        using var deleteRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/files/host-files/{created.Id:D}/delete",
            adminToken,
            new { });
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);

        await VerifyDeletedBlobCleanupAsync(
            factory,
            created.Id,
            payload,
            cancellationToken);

        using var missingDownloadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files/{created.Id:D}/content");
        missingDownloadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingDownloadResponse = await client.SendAsync(
            missingDownloadRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingDownloadResponse.StatusCode);
    }

    private static async Task VerifyExactHostFileActionPermissionBoundariesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var payload = Encoding.UTF8.GetBytes($"files-boundary-{Guid.NewGuid():N}");
        var fileName = "boundary.txt";

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
            adminToken);
        using var uploadResponse = await client.SendAsync(uploadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, uploadResponse.StatusCode);
        var created = await uploadResponse.Content.ReadFromJsonAsync<HostFileResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [HostFilePermissions.Read, HostFilePermissions.Download],
            cancellationToken);
        using var readOnlyDownloadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files/{created.Id:D}/content");
        readOnlyDownloadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            readOnlyToken);
        using var readOnlyDownloadResponse = await client.SendAsync(
            readOnlyDownloadRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, readOnlyDownloadResponse.StatusCode);

        var listOnlyToken = await factory.CreateHostAccessTokenAsync(
            [HostFilePermissions.Read],
            cancellationToken);
        await AssertHostFilePermissionDeniedAsync(
            client,
            listOnlyToken,
            HttpMethod.Get,
            $"/api/v1/files/host-files/{created.Id:D}/content",
            cancellationToken);
        using var deniedUploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/files/host-files")
        {
            Content = uploadContent,
        };
        deniedUploadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            listOnlyToken);
        using var deniedUploadResponse = await client.SendAsync(
            deniedUploadRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedUploadResponse.StatusCode);
        await AssertHostFilePermissionDeniedAsync(
            client,
            listOnlyToken,
            HttpMethod.Post,
            $"/api/v1/files/host-files/{created.Id:D}/delete",
            cancellationToken,
            new { });

        var uploadToken = await factory.CreateHostAccessTokenAsync(
            [
                HostFilePermissions.Read,
                HostFilePermissions.Upload,
            ],
            cancellationToken);
        using var uploadOnlyContent = new MultipartFormDataContent();
        var uploadOnlyFile = new ByteArrayContent(Encoding.UTF8.GetBytes("upload-only"));
        uploadOnlyFile.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadOnlyContent.Add(uploadOnlyFile, "file", "upload-only.txt");
        using var uploadOnlyRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/files/host-files")
        {
            Content = uploadOnlyContent,
        };
        uploadOnlyRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            uploadToken);
        using var uploadOnlyResponse = await client.SendAsync(uploadOnlyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, uploadOnlyResponse.StatusCode);
        await AssertHostFilePermissionDeniedAsync(
            client,
            uploadToken,
            HttpMethod.Post,
            $"/api/v1/files/host-files/{created.Id:D}/delete",
            cancellationToken,
            new { });

        var deleteTargetPayload = Encoding.UTF8.GetBytes($"delete-target-{Guid.NewGuid():N}");
        using var deleteSeedContent = new MultipartFormDataContent();
        var deleteSeedFile = new ByteArrayContent(deleteTargetPayload);
        deleteSeedFile.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        deleteSeedContent.Add(deleteSeedFile, "file", "delete-target.txt");
        using var deleteSeedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/files/host-files")
        {
            Content = deleteSeedContent,
        };
        deleteSeedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var deleteSeedResponse = await client.SendAsync(deleteSeedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, deleteSeedResponse.StatusCode);
        var deleteTarget = await deleteSeedResponse.Content.ReadFromJsonAsync<HostFileResponse>(
            cancellationToken);
        Assert.IsNotNull(deleteTarget);

        var deleteToken = await factory.CreateHostAccessTokenAsync(
            [
                HostFilePermissions.Read,
                HostFilePermissions.Delete,
            ],
            cancellationToken);
        using var deleteRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/files/host-files/{deleteTarget.Id:D}/delete",
            deleteToken,
            new { });
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    private static async Task AssertHostFilePermissionDeniedAsync(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertHostFilePermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        TRequest body)
        where TRequest : class
    {
        using var request = CreateBearerJsonRequest(method, path, accessToken, body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDeletedBlobCleanupAsync(
        FullNetApiFactory factory,
        Guid fileId,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var tombstone = await query
                .QuerySingleOrDefaultAsync<DeletedHostFileBlobRecord>(
                    new SqlStatement(
                        "test.files.find-deleted-host-file",
                        """
                        SELECT Id, ProviderKey, StorageKey, DeletedAtUtc
                        FROM fn_files_file
                        WHERE Id = @FileId
                          AND TenantId IS NULL
                          AND DeletedAtUtc IS NOT NULL
                        """,
                        SqlDataScope.HostOnly),
                    new { FileId = fileId },
                    cancellationToken);
            Assert.IsNotNull(tombstone);

            Assert.AreEqual(LocalHostFileBlobStorage.Key, tombstone.ProviderKey);
            var storageProviders = scope.ServiceProvider
                .GetRequiredService<FileStorageProviderRegistry>();
            var blobStorage = storageProviders.Resolve(tombstone.ProviderKey);
            await using (var content = new MemoryStream(payload, writable: false))
            {
                // 重建同步删除失败后的真实残态，后台任务必须先删 Blob 再清除墓碑。
                await blobStorage.SaveAsync(
                    tombstone.StorageKey,
                    content,
                    cancellationToken);
            }

            var runner = ActivatorUtilities.CreateInstance<
                DeletedHostFileBlobCleanupRunner>(scope.ServiceProvider);
            var result = await runner.RunOnceAsync(
                new DeletedHostFileBlobCleanupOptions
                {
                    Enabled = true,
                    BatchSize = 50,
                    MaxBatchesPerRun = 10,
                },
                cancellationToken);
            Assert.IsTrue(result.Purged >= 1);
            Assert.AreEqual(0, result.BlobFailures);

            var remaining = await query.QuerySingleOrDefaultAsync<long>(
                new SqlStatement(
                    "test.files.count-deleted-host-file",
                    """
                    SELECT COUNT(1)
                    FROM fn_files_file
                    WHERE Id = @FileId
                      AND TenantId IS NULL
                    """,
                    SqlDataScope.HostOnly),
                new { FileId = fileId },
                cancellationToken);
            Assert.AreEqual(0L, remaining);
            _ = await Assert.ThrowsAsync<FileNotFoundException>(
                () => blobStorage.OpenReadAsync(
                    tombstone.StorageKey,
                    cancellationToken));

            var secondResult = await runner.RunOnceAsync(
                new DeletedHostFileBlobCleanupOptions
                {
                    Enabled = true,
                    BatchSize = 50,
                    MaxBatchesPerRun = 10,
                },
                cancellationToken);
            Assert.AreEqual(0, secondResult.Scanned);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private sealed record PagedHostFileResponses(
        HostFileResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
