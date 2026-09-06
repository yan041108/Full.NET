using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Data.Abstractions;
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
        await VerifyVirtualFoldersMetadataAndReferencesAsync(
            factory,
            client,
            cancellationToken);
        await VerifyBatchUploadDeleteAndPreviewAsync(
            factory,
            client,
            cancellationToken);
        await OpenApiFilesHostFilesContractAssertions.VerifyAsync(client, cancellationToken);
        await OpenApiFilesHostFoldersContractAssertions.VerifyAsync(client, cancellationToken);
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

    private static async Task VerifyVirtualFoldersMetadataAndReferencesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var folderName = $"integration-folder-{Guid.NewGuid():N}";

        using var createFolderRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/files/host-folders",
            adminToken,
            new CreateHostFolderRequest(null, folderName, 0));
        using var createFolderResponse = await client.SendAsync(
            createFolderRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, createFolderResponse.StatusCode);
        var folder = await createFolderResponse.Content.ReadFromJsonAsync<HostFolderResponse>(
            cancellationToken);
        Assert.IsNotNull(folder);
        Assert.AreEqual(folderName, folder.Name);
        Assert.IsTrue(folder.Revision >= 1);

        using var treeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/files/host-folders/tree");
        treeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var treeResponse = await client.SendAsync(treeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, treeResponse.StatusCode);
        var tree = await treeResponse.Content.ReadFromJsonAsync<HostFolderTreeNode[]>(
            cancellationToken);
        Assert.IsNotNull(tree);
        Assert.IsTrue(ContainsFolder(tree, folder.Id, folderName));

        var payload = Encoding.UTF8.GetBytes($"folder-file-{Guid.NewGuid():N}");
        var fileName = "folder-file.txt";
        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", fileName);
        uploadContent.Add(new StringContent(folder.Id.ToString("D")), "folderId");

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
        Assert.AreEqual(folder.Id, created.FolderId);
        Assert.IsTrue(created.Revision >= 1);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files?folderId={folder.Id:D}&page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedHostFileResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));

        var renamed = "folder-file-renamed.txt";
        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/files/host-files/{created.Id:D}/update",
            adminToken,
            new UpdateHostFileMetadataRequest(
                created.Revision,
                renamed,
                folder.Id));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostFileResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual(renamed, updated.OriginalFileName);
        Assert.IsTrue(updated.Revision > created.Revision);

        using var staleUpdateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/files/host-files/{created.Id:D}/update",
            adminToken,
            new UpdateHostFileMetadataRequest(
                created.Revision,
                "stale.txt",
                folder.Id));
        using var staleUpdateResponse = await client.SendAsync(
            staleUpdateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleUpdateResponse.StatusCode);
        using var staleProblem = JsonDocument.Parse(
            await staleUpdateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            FilesErrorCodes.RevisionConflict,
            staleProblem.RootElement.GetProperty("code").GetString());

        var consumerReferenceId = Guid.CreateVersion7();
        var idempotencyKey =
            HostFileReferenceClaimIdempotencyKeys.DocumentVersion(consumerReferenceId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            currentTenant.SetHost();
            try
            {
                var claimService = scope.ServiceProvider
                    .GetRequiredService<IHostFileReferenceClaimService>();
                var claimResult = await claimService.ClaimAsync(
                    new HostFileReferenceClaimRequest(
                        idempotencyKey,
                        HostFileReferenceClaimConsumerModules.Document,
                        consumerReferenceId,
                        created.Id),
                    cancellationToken);
                Assert.IsTrue(claimResult.IsSuccess);
                var confirmResult = await claimService.ConfirmAsync(
                    idempotencyKey,
                    cancellationToken);
                Assert.IsTrue(confirmResult.IsSuccess);
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        using var referencesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files/{created.Id:D}/references?page=1&pageSize=20");
        referencesRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var referencesResponse = await client.SendAsync(
            referencesRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, referencesResponse.StatusCode);
        var references = await referencesResponse.Content
            .ReadFromJsonAsync<PagedResult<HostFileReferenceClaimResponse>>(cancellationToken);
        Assert.IsNotNull(references);
        Assert.IsTrue(references.Items.Any(item =>
            item.IdempotencyKey == idempotencyKey
            && item.ConsumerModule == HostFileReferenceClaimConsumerModules.Document
            && item.ConsumerReferenceId == consumerReferenceId
            && item.State == HostFileReferenceClaimStates.Active));

        await using (var releaseScope = factory.Services.CreateAsyncScope())
        {
            var claimService = releaseScope.ServiceProvider
                .GetRequiredService<IHostFileReferenceClaimService>();
            var releaseResult = await claimService.ReleaseAsync(
                idempotencyKey,
                cancellationToken);
            Assert.IsTrue(releaseResult.IsSuccess);
        }

        using var deleteFolderRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/files/host-folders/{folder.Id:D}/delete",
            adminToken,
            new DeleteHostFolderRequest(folder.Revision));
        using var deleteFolderResponse = await client.SendAsync(
            deleteFolderRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, deleteFolderResponse.StatusCode);
        using var deleteFolderProblem = JsonDocument.Parse(
            await deleteFolderResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            FilesErrorCodes.FolderNotEmpty,
            deleteFolderProblem.RootElement.GetProperty("code").GetString());

        using var deleteFileRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/files/host-files/{created.Id:D}/delete",
            adminToken,
            new { });
        using var deleteFileResponse = await client.SendAsync(
            deleteFileRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, deleteFileResponse.StatusCode);

        using var deleteEmptyFolderRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/files/host-folders/{folder.Id:D}/delete",
            adminToken,
            new DeleteHostFolderRequest(folder.Revision));
        using var deleteEmptyFolderResponse = await client.SendAsync(
            deleteEmptyFolderRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, deleteEmptyFolderResponse.StatusCode);
    }

    private static async Task VerifyBatchUploadDeleteAndPreviewAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var firstPayload = Encoding.UTF8.GetBytes($"batch-one-{Guid.NewGuid():N}");
        var secondPayload = Encoding.UTF8.GetBytes($"batch-two-{Guid.NewGuid():N}");

        using var uploadContent = new MultipartFormDataContent();
        var firstFile = new ByteArrayContent(firstPayload);
        firstFile.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(firstFile, "files", "batch-one.txt");
        var secondFile = new ByteArrayContent(secondPayload);
        secondFile.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(secondFile, "files", "batch-two.txt");

        using var batchUploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/files/host-files/batch-upload")
        {
            Content = uploadContent,
        };
        batchUploadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var batchUploadResponse = await client.SendAsync(
            batchUploadRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, batchUploadResponse.StatusCode);
        var batchUpload = await batchUploadResponse.Content
            .ReadFromJsonAsync<BatchUploadHostFilesResponse>(cancellationToken);
        Assert.IsNotNull(batchUpload);
        Assert.AreEqual(2, batchUpload.SucceededCount);
        Assert.AreEqual(2, batchUpload.Results.Count(item => item.Succeeded));
        var uploadedIds = batchUpload.Results
            .Where(item => item.Succeeded && item.File is not null)
            .Select(item => item.File!.Id)
            .ToArray();
        Assert.AreEqual(2, uploadedIds.Length);

        using var previewRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/files/host-files/{uploadedIds[0]:D}/preview");
        previewRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var previewResponse = await client.SendAsync(previewRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.AreEqual(
            "text/plain",
            previewResponse.Content.Headers.ContentType?.MediaType);
        var previewBody = await previewResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        CollectionAssert.AreEqual(firstPayload, previewBody);

        var consumerReferenceId = Guid.CreateVersion7();
        var idempotencyKey =
            HostFileReferenceClaimIdempotencyKeys.DocumentVersion(consumerReferenceId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var claimService = scope.ServiceProvider
                .GetRequiredService<IHostFileReferenceClaimService>();
            var claimResult = await claimService.ClaimAsync(
                new HostFileReferenceClaimRequest(
                    idempotencyKey,
                    HostFileReferenceClaimConsumerModules.Document,
                    consumerReferenceId,
                    uploadedIds[1]),
                cancellationToken);
            Assert.IsTrue(claimResult.IsSuccess);
            var confirmResult = await claimService.ConfirmAsync(
                idempotencyKey,
                cancellationToken);
            Assert.IsTrue(confirmResult.IsSuccess);
        }

        using var batchDeleteRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/files/host-files/batch-delete",
            adminToken,
            new BatchDeleteHostFilesRequest(uploadedIds));
        using var batchDeleteResponse = await client.SendAsync(
            batchDeleteRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, batchDeleteResponse.StatusCode);
        var batchDelete = await batchDeleteResponse.Content
            .ReadFromJsonAsync<BatchDeleteHostFilesResponse>(cancellationToken);
        Assert.IsNotNull(batchDelete);
        Assert.AreEqual(1, batchDelete.SucceededCount);
        Assert.IsFalse(batchDelete.Results[1].Succeeded);
        Assert.AreEqual(
            FilesErrorCodes.FileReferenced,
            batchDelete.Results[1].ErrorCode);

        await using (var releaseScope = factory.Services.CreateAsyncScope())
        {
            var claimService = releaseScope.ServiceProvider
                .GetRequiredService<IHostFileReferenceClaimService>();
            var releaseResult = await claimService.ReleaseAsync(
                idempotencyKey,
                cancellationToken);
            Assert.IsTrue(releaseResult.IsSuccess);
        }

        using var secondDeleteRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/files/host-files/batch-delete",
            adminToken,
            new BatchDeleteHostFilesRequest([uploadedIds[1]]));
        using var secondDeleteResponse = await client.SendAsync(
            secondDeleteRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondDeleteResponse.StatusCode);
        var secondDelete = await secondDeleteResponse.Content
            .ReadFromJsonAsync<BatchDeleteHostFilesResponse>(cancellationToken);
        Assert.IsNotNull(secondDelete);
        Assert.AreEqual(1, secondDelete.SucceededCount);
    }

    private static bool ContainsFolder(
        IEnumerable<HostFolderTreeNode> nodes,
        Guid folderId,
        string folderName)
    {
        foreach (var node in nodes)
        {
            if (node.Id == folderId && node.Name == folderName)
            {
                return true;
            }

            if (ContainsFolder(node.Children, folderId, folderName))
            {
                return true;
            }
        }

        return false;
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
