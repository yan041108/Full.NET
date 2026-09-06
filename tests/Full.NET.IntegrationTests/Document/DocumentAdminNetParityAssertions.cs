using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.IntegrationTests.Document;

internal static class DocumentAdminNetParityAssertions
{
    private const string ItemsPath = "/api/v1/document/host/items";
    private const string ItemsPath = "/api/v1/document/host/items";
    private const string RecycleBinPath = "/api/v1/document/host/recycle-bin";
    private const string PermissionsPath = "/api/v1/document/host/permissions";
    private const string SharesPath = "/api/v1/document/host/shares";
    private const string StatisticsPath = "/api/v1/document/host/statistics";
    private const string PreviewTasksPath = "/api/v1/document/host/preview-tasks";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyHostOnlyFailClosedAsync(factory, client, cancellationToken);

        var manager = await factory.CreateHostIdentityAsync(
            $"document-parity-{Guid.NewGuid():N}",
            FullManagerPermissions(),
            cancellationToken);
        var reader = await factory.CreateHostIdentityAsync(
            $"document-parity-reader-{Guid.NewGuid():N}",
            [
                HostDocumentPermissions.Read,
                HostDocumentRecycleBinPermissions.Read,
                HostDocumentSharePermissions.Read,
                HostDocumentStatisticsPermissions.Read,
                HostDocumentAccessLogPermissions.Read,
                HostDocumentPreviewTaskPermissions.Read,
                HostDocumentPermissionManagementPermissions.Read,
            ],
            cancellationToken);
        var delegateUser = await factory.CreateHostIdentityAsync(
            $"document-parity-delegate-{Guid.NewGuid():N}",
            [HostDocumentPermissions.Read],
            cancellationToken);

        var document = await CreateDocumentAsync(client, manager.AccessToken, cancellationToken);
        await VerifyPermissionDenyMatrixAsync(client, reader.AccessToken, document.Id, cancellationToken);
        await VerifyPermissionsAsync(
            client,
            manager,
            delegateUser,
            document.Id,
            cancellationToken);
        await VerifySharesAsync(client, manager.AccessToken, document.Id, cancellationToken);
        await VerifyStatisticsAsync(client, manager.AccessToken, cancellationToken);
        await VerifyPreviewTasksAsync(client, manager, document, cancellationToken);
        await VerifyRecycleBinAsync(client, manager.AccessToken, document, cancellationToken);
        await OpenApiDocumentHostPermissionsContractAssertions.VerifyAsync(client, cancellationToken);
        await OpenApiDocumentHostRecycleBinContractAssertions.VerifyAsync(client, cancellationToken);
        await OpenApiDocumentHostStatisticsContractAssertions.VerifyAsync(client, cancellationToken);
        await OpenApiDocumentHostAccessLogsContractAssertions.VerifyAsync(client, cancellationToken);
        await OpenApiDocumentHostPreviewTasksContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static IReadOnlyCollection<string> FullManagerPermissions() =>
    [
        HostDocumentPermissions.Read,
        HostDocumentPermissions.Create,
        HostDocumentPermissions.Update,
        HostDocumentPermissions.AddVersion,
        HostDocumentPermissions.Delete,
        HostDocumentPermissions.Restore,
        HostDocumentRecycleBinPermissions.Read,
        HostDocumentRecycleBinPermissions.Restore,
        HostDocumentRecycleBinPermissions.Purge,
        HostDocumentPermissionManagementPermissions.Read,
        HostDocumentPermissionManagementPermissions.Set,
        HostDocumentSharePermissions.Read,
        HostDocumentSharePermissions.Create,
        HostDocumentSharePermissions.UpdateStatus,
        HostDocumentStatisticsPermissions.Read,
        HostDocumentPreviewTaskPermissions.Read,
        HostDocumentPreviewTaskPermissions.Create,
    ];

    private static async Task VerifyHostOnlyFailClosedAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var anonymous = await client.GetAsync(RecycleBinPath, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var wrongPermission = await factory.CreateHostAccessTokenAsync(
            ["platform.dashboard.read"],
            cancellationToken);
        using var forbidden = await client.SendAsync(
            Authorized(HttpMethod.Get, StatisticsPath, wrongPermission),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task VerifyPermissionDenyMatrixAsync(
        HttpClient client,
        string readerToken,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        using (var setResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       PermissionsPath,
                       readerToken,
                       new SetHostDocumentPermissionsRequest(
                           documentId,
                           [new HostDocumentPermissionEntry(Guid.NewGuid(), "read")])),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, setResponse.StatusCode);
        }

        using (var createShareResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       SharesPath,
                       readerToken,
                       new CreateHostDocumentShareRequest(documentId, ValidDays: 7)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, createShareResponse.StatusCode);
        }

        using (var createPreviewTaskResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       PreviewTasksPath,
                       readerToken,
                       new CreateHostDocumentPreviewTaskRequest(documentId, null)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, createPreviewTaskResponse.StatusCode);
        }
    }

    private static async Task VerifyPermissionsAsync(
        HttpClient client,
        HostTestIdentity manager,
        HostTestIdentity delegateUser,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        using (var setResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       PermissionsPath,
                       manager.AccessToken,
                       new SetHostDocumentPermissionsRequest(
                           documentId,
                           [new HostDocumentPermissionEntry(delegateUser.UserId, "read")])),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);
            var permissions = await setResponse.Content
                .ReadFromJsonAsync<IReadOnlyList<HostDocumentPermissionResponse>>(cancellationToken);
            Assert.IsNotNull(permissions);
            Assert.AreEqual(1, permissions.Count);
            Assert.AreEqual(delegateUser.UserId, permissions[0].UserId);
            Assert.AreEqual("read", permissions[0].PermissionLevel);
        }

        using var listResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, $"{PermissionsPath}/by-document/{documentId:D}", manager.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await listResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<HostDocumentPermissionResponse>>(cancellationToken);
        Assert.IsNotNull(listed);
        Assert.AreEqual(1, listed.Count);
    }

    private static async Task VerifySharesAsync(
        HttpClient client,
        string token,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        HostDocumentShareResponse share;
        using (var createResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       SharesPath,
                       token,
                       new CreateHostDocumentShareRequest(documentId, ValidDays: 3)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
            share = (await createResponse.Content.ReadFromJsonAsync<HostDocumentShareResponse>(cancellationToken))!;
            Assert.IsFalse(share.HasPassword);
        }

        using (var listResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, SharesPath + "?page=1&pageSize=20", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostDocumentShareResponse>>(
                cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Items.Any(entry => entry.Id == share.Id));
        }

        using (var disableResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{SharesPath}/{share.Id:D}/status",
                       token,
                       new UpdateHostDocumentShareStatusRequest(false, share.Version)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
            var disabled = await disableResponse.Content.ReadFromJsonAsync<HostDocumentShareResponse>(cancellationToken);
            Assert.IsNotNull(disabled);
            Assert.IsFalse(disabled.IsEnabled);
        }
    }

    private static async Task VerifyStatisticsAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            Authorized(HttpMethod.Get, StatisticsPath, token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var statistics = await response.Content.ReadFromJsonAsync<HostDocumentStatisticsResponse>(cancellationToken);
        Assert.IsNotNull(statistics);
        Assert.IsTrue(statistics.Summary.TotalItems >= 1);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsFalse(json.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task VerifyPreviewTasksAsync(
        HttpClient client,
        HostTestIdentity manager,
        HostDocumentItemResponse document,
        CancellationToken cancellationToken)
    {
        await UploadOfficeVersionAsync(client, manager.AccessToken, document.Id, cancellationToken);

        HostDocumentPreviewTaskResponse task;
        using (var createResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       PreviewTasksPath,
                       manager.AccessToken,
                       new CreateHostDocumentPreviewTaskRequest(document.Id, null)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
            task = (await createResponse.Content.ReadFromJsonAsync<HostDocumentPreviewTaskResponse>(cancellationToken))!;
            Assert.AreEqual(document.Id, task.DocumentItemId);
            Assert.AreEqual(HostDocumentPreviewTaskStatusKeys.Pending, task.StatusKey);
            Assert.AreEqual(DocumentOfficePreviewConversionProviderKeys.Disabled, task.ProviderKey);
            Assert.AreEqual(manager.UserId, task.RequestedByUserId);
        }

        using (var getResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, $"{PreviewTasksPath}/{task.Id:D}", manager.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
            var loaded = await getResponse.Content.ReadFromJsonAsync<HostDocumentPreviewTaskResponse>(cancellationToken);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(task.Id, loaded.Id);
        }

        using (var listResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       $"{PreviewTasksPath}?page=1&pageSize=20&documentItemId={document.Id:D}",
                       manager.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostDocumentPreviewTaskResponse>>(
                cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Items.Any(entry => entry.Id == task.Id));
        }
    }

    private static async Task UploadOfficeVersionAsync(
        HttpClient client,
        string token,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes($"office-{Guid.NewGuid():N}");
        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        uploadContent.Add(fileContent, "file", "preview.docx");
        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ItemsPath}/{itemId:D}/versions/upload")
        {
            Content = uploadContent,
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var uploadResponse = await client.SendAsync(uploadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, uploadResponse.StatusCode);
    }

    private static async Task VerifyRecycleBinAsync(
        HttpClient client,
        string token,
        HostDocumentItemResponse document,
        CancellationToken cancellationToken)
    {
        using (var deleteResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{ItemsPath}/{document.Id:D}/delete",
                       token,
                       new DeleteHostDocumentItemRequest(document.Version)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        }

        HostDocumentItemResponse deletedItem;
        using (var listResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, RecycleBinPath + "?page=1&pageSize=20", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostDocumentItemResponse>>(
                cancellationToken);
            Assert.IsNotNull(page);
            deletedItem = page.Items.Single(entry => entry.Id == document.Id);
        }

        HostDocumentItemResponse restoredItem;
        using (var restoreResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RecycleBinPath}/{document.Id:D}/restore",
                       token,
                       new RestoreHostDocumentItemRequest(deletedItem.Version)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, restoreResponse.StatusCode);
            restoredItem = (await restoreResponse.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken))!;
        }

        using (var deleteAgainResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{ItemsPath}/{document.Id:D}/delete",
                       token,
                       new DeleteHostDocumentItemRequest(restoredItem.Version)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, deleteAgainResponse.StatusCode);
        }

        using (var purgeResponse = await client.SendAsync(
                   Authorized(HttpMethod.Post, $"{RecycleBinPath}/{document.Id:D}/purge", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, purgeResponse.StatusCode);
            var purged = await purgeResponse.Content.ReadFromJsonAsync<bool>(cancellationToken);
            Assert.IsTrue(purged);
        }

        using (var listAfterPurgeResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, RecycleBinPath + "?page=1&pageSize=20", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listAfterPurgeResponse.StatusCode);
            var page = await listAfterPurgeResponse.Content
                .ReadFromJsonAsync<PagedResult<HostDocumentItemResponse>>(cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsFalse(page.Items.Any(entry => entry.Id == document.Id));
        }
    }

    private static async Task<HostDocumentItemResponse> CreateDocumentAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                ItemsPath,
                token,
                new CreateHostDocumentItemRequest(
                    $"Parity {Guid.NewGuid():N}",
                    "Admin.NET parity integration")),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(document);
        return document;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(HttpMethod method, string path, string token, T body)
    {
        var request = Authorized(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }
}
