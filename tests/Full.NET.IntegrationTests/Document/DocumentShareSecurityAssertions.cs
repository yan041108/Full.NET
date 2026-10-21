using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

/// <summary>文档分享：令牌、过期与越权拒绝。</summary>
internal static class DocumentShareSecurityAssertions
{
    private const string ItemsPath = "/api/v1/document/host/items";
    private const string SharesPath = "/api/v1/document/host/shares";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await OpenApiDocumentHostSharesContractAssertions.VerifyAsync(client, cancellationToken);
        var identity = await factory.CreateHostIdentityAsync(
            $"document-share-{Guid.NewGuid():N}",
            [
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Create,
                HostDocumentPermissions.AddVersion,
                HostDocumentPermissions.Delete,
                HostDocumentSharePermissions.Read,
                HostDocumentSharePermissions.Create,
                HostDocumentSharePermissions.UpdateStatus,
            ],
            cancellationToken);

        var document = await CreateDocumentAsync(client, identity.AccessToken, cancellationToken);
        const string password = "Share@2026!Secure";
        var share = await CreateShareAsync(
            client,
            identity.AccessToken,
            document.Id,
            password,
            cancellationToken);

        await VerifyStoredHashAsync(factory, share.Id, password, cancellationToken);
        await VerifyManagementResponseAsync(client, identity.AccessToken, share.Id, cancellationToken);
        await VerifyLegacyGetIsClosedAsync(client, share.ShareCode, cancellationToken);
        await VerifyWrongPasswordDoesNotConsumeAsync(factory, client, share, cancellationToken);
        await VerifyConcurrentLimitIsAtomicAsync(factory, client, share, password, cancellationToken);
        await VerifyPreviewGrantChainAsync(factory, client, identity.AccessToken, password, cancellationToken);
    }

    /// <summary>预览创建操作只能消费一次访问，重试和轮询不续期，且凭据随分享和文档撤销。</summary>
    private static async Task VerifyPreviewGrantChainAsync(FullNetApiFactory factory, HttpClient client,
        string token, string password, CancellationToken cancellationToken)
    {
        var document = await CreateDocumentAsync(client, token, cancellationToken);
        using (var upload = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent("office-preview-grant-test"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            upload.Add(file, "file", "preview.docx");
            using var request = Authorized(HttpMethod.Post, $"{ItemsPath}/{document.Id:D}/versions/upload", token);
            request.Content = upload;
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            document = (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken))!;
            Assert.IsNotNull(document);
        }
        var share = await CreateShareAsync(client, token, document.Id, password, cancellationToken);
        var root = $"/api/v1/document/public/shares/{share.ShareCode}";
        using var accessResponse = await client.PostAsJsonAsync(root + "/access", new AccessHostDocumentShareRequest(password), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, accessResponse.StatusCode);
        var access = await accessResponse.Content.ReadFromJsonAsync<HostDocumentShareAccessResponse>(cancellationToken);
        Assert.IsNotNull(access);
        Assert.IsFalse(string.IsNullOrEmpty(access.PreviewGrant));
        var creation = new AccessHostDocumentShareRequest { PreviewGrant = access.PreviewGrant };
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync(root + "/preview-task", creation, cancellationToken)));
        HostDocumentPreviewTaskResponse? task = null;
        try
        {
            foreach (var response in attempts)
            {
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                var current = await response.Content.ReadFromJsonAsync<HostDocumentPreviewTaskResponse>(cancellationToken);
                Assert.IsNotNull(current);
                Assert.IsNull(current.RequestedByUserId, "匿名预览不能伪造用户身份。");
                Assert.IsNotNull(current.PreviewPollUntilUtc);
                Assert.IsFalse(string.IsNullOrEmpty(current.PreviewGrant));
                if (task is not null)
                {
                    Assert.AreEqual(task.Id, current.Id, "并发创建必须复用固定操作标识。");
                    Assert.AreEqual(task.PreviewPollUntilUtc, current.PreviewPollUntilUtc, "重试不能延长原始截止时间。");
                }
                task = current;
            }
        }
        finally { foreach (var response in attempts) response.Dispose(); }
        Assert.IsNotNull(task);
        Assert.AreEqual(1, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
        long managementVersion;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var count = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(
                new SqlStatement("test.document_share.preview_task_count",
                    "SELECT COUNT(*) FROM fn_document_preview_task WHERE DocumentItemId=@Id", SqlDataScope.HostOnly),
                new { Id = document.Id }, cancellationToken);
            Assert.AreEqual(1L, count);
            managementVersion = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(
                new SqlStatement("test.document_share.current_version",
                    "SELECT Version FROM fn_document_share WHERE TenantId IS NULL AND Id=@Id", SqlDataScope.HostOnly),
                new { Id = share.Id }, cancellationToken);
        }
        var poll = new AccessHostDocumentShareRequest { PreviewGrant = task.PreviewGrant };
        var status = root + $"/preview-tasks/{task.Id:D}";
        using (var response = await client.PostAsJsonAsync(status, poll, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var loaded = await response.Content.ReadFromJsonAsync<HostDocumentPreviewTaskResponse>(cancellationToken);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(task.Id, loaded.Id);
            Assert.AreEqual(document.Id, loaded.DocumentItemId);
        }
        await DocumentAnonymousPreviewExecutionAssertions.VerifyAsync(factory, client, document, task.Id, status, poll, cancellationToken);
        using (var response = await client.PostAsJsonAsync(status, creation, cancellationToken))
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, "创建凭据不能读取任务。");
        using (var response = await client.PostAsJsonAsync(root + "/preview-task", poll, cancellationToken))
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, "任务凭据不能创建另一任务。");
        using (var response = await client.PostAsJsonAsync(root + $"/preview-tasks/{Guid.CreateVersion7():D}", poll, cancellationToken))
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, "任务凭据不能跨任务使用。");
        using (var response = await client.PostAsJsonAsync(root + "/content", poll, cancellationToken))
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, "预览凭据不能替代原始下载口令。");
        Assert.AreEqual(1, await QueryAccessCountAsync(factory, share.Id, cancellationToken));

        // 另一个有效分享用于证明文档删除也会撤销凭据，不能把拒绝归因于上一分享已停用。
        var second = await CreateShareAsync(client, token, document.Id, password, cancellationToken);
        var secondRoot = $"/api/v1/document/public/shares/{second.ShareCode}";
        using var secondAccessResponse = await client.PostAsJsonAsync(secondRoot + "/access", new AccessHostDocumentShareRequest(password), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondAccessResponse.StatusCode);
        var secondAccess = await secondAccessResponse.Content.ReadFromJsonAsync<HostDocumentShareAccessResponse>(cancellationToken);
        Assert.IsNotNull(secondAccess);
        using var secondCreateResponse = await client.PostAsJsonAsync(secondRoot + "/preview-task",
            new AccessHostDocumentShareRequest { PreviewGrant = secondAccess.PreviewGrant }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, secondCreateResponse.StatusCode);
        var secondTask = await secondCreateResponse.Content.ReadFromJsonAsync<HostDocumentPreviewTaskResponse>(cancellationToken);
        Assert.IsNotNull(secondTask);
        var secondPoll = new AccessHostDocumentShareRequest { PreviewGrant = secondTask.PreviewGrant };
        var secondStatus = secondRoot + $"/preview-tasks/{secondTask.Id:D}";
        using (var response = await client.PostAsJsonAsync(secondStatus, secondPoll, cancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.SendAsync(AuthorizedJson(HttpMethod.Post, $"{SharesPath}/{share.Id:D}/status",
                   token, new UpdateHostDocumentShareStatusRequest(false, managementVersion)), cancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.PostAsJsonAsync(status, poll, cancellationToken))
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, "停用分享后旧凭据立即失效。");
        using (var response = await client.SendAsync(AuthorizedJson(HttpMethod.Post, $"{ItemsPath}/{document.Id:D}/delete",
                   token, new DeleteHostDocumentItemRequest(document.Version)), cancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.PostAsJsonAsync(secondStatus, secondPoll, cancellationToken))
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, "软删除文档后另一个仍启用分享的凭据也必须拒绝。");
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
                    $"Share security {Guid.NewGuid():N}",
                    "匿名分享安全集成测试")),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(document);
        return document;
    }

    private static async Task<HostDocumentShareResponse> CreateShareAsync(
        HttpClient client,
        string token,
        Guid documentId,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                SharesPath,
                token,
                new CreateHostDocumentShareRequest(
                    documentId,
                    ValidDays: 7,
                    Password: password,
                    MaxAccessCount: 1)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var share = await response.Content.ReadFromJsonAsync<HostDocumentShareResponse>(cancellationToken);
        Assert.IsNotNull(share);
        Assert.IsTrue(share.HasPassword);
        Assert.IsNull(share.Password);
        return share;
    }

    private static async Task VerifyStoredHashAsync(
        FullNetApiFactory factory,
        Guid shareId,
        string password,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var storedHash = await query.QuerySingleOrDefaultAsync<string>(
            new SqlStatement(
                "test.document_share.password_hash",
                "SELECT PasswordHash FROM fn_document_share WHERE Id = @Id AND TenantId IS NULL",
                SqlDataScope.HostOnly),
            new { Id = shareId },
            cancellationToken);

        Assert.IsFalse(string.IsNullOrWhiteSpace(storedHash));
        Assert.AreNotEqual(password, storedHash);
        Assert.IsFalse(storedHash.Contains(password, StringComparison.Ordinal));
    }

    private static async Task VerifyManagementResponseAsync(
        HttpClient client,
        string token,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            Authorized(HttpMethod.Get, SharesPath + "?page=1&pageSize=20", token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsTrue(json.Contains(shareId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("\"password\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task VerifyLegacyGetIsClosedAsync(
        HttpClient client,
        string shareCode,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{SharesPath}/by-code/{shareCode}", cancellationToken);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private static async Task VerifyWrongPasswordDoesNotConsumeAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostDocumentShareResponse share,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/document/public/shares/{share.ShareCode}/access",
            new AccessHostDocumentShareRequest("WrongPassword!2026"),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
    }

    private static async Task VerifyConcurrentLimitIsAtomicAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostDocumentShareResponse share,
        string password,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/document/public/shares/{share.ShareCode}/access";
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync(
                path,
                new AccessHostDocumentShareRequest(password),
                cancellationToken))
            .ToArray();
        var responses = await Task.WhenAll(attempts);
        try
        {
            Assert.AreEqual(
                1,
                responses.Count(response => response.StatusCode == HttpStatusCode.OK),
                "访问上限为 1 时，并发请求必须仅成功一次。");
            Assert.IsTrue(
                responses.Where(response => response.StatusCode != HttpStatusCode.OK)
                    .All(response => response.StatusCode == HttpStatusCode.UnprocessableEntity),
                "未取得访问配额的请求必须稳定返回业务规则失败。");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        Assert.AreEqual(1, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
    }

    private static async Task<int> QueryAccessCountAsync(
        FullNetApiFactory factory,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<int>(
                new SqlStatement(
                    "test.document_share.access_count",
                    "SELECT AccessCount FROM fn_document_share WHERE Id = @Id AND TenantId IS NULL",
                    SqlDataScope.HostOnly),
                new { Id = shareId },
                cancellationToken);
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
