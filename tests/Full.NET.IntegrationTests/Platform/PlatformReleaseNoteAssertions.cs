using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Platform.Contracts;

namespace Full.NET.IntegrationTests.Platform;

/// <summary>平台更新日志纵向切片验收夹具。</summary>
internal static class PlatformReleaseNoteAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await OpenApiPlatformReleaseNotesContractAssertions.VerifyAsync(
            client,
            cancellationToken);

        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        var draft = await CreateDraftAsync(
            client,
            adminToken,
            "2.0.0",
            "2.0 发布说明",
            "第二条更新日志",
            cancellationToken);
        Assert.AreEqual(ReleaseNoteStatuses.Draft, draft.Status);
        Assert.AreEqual(1, draft.Version);
        Assert.AreEqual(2_000_000_000L, draft.VersionSortKey);

        var olderDraft = await CreateDraftAsync(
            client,
            adminToken,
            "1.0.0",
            "1.0 发布说明",
            "第一条更新日志",
            cancellationToken);

        var publishedOlder = await PublishAsync(
            client,
            adminToken,
            olderDraft.Id,
            olderDraft.Version,
            cancellationToken);
        Assert.AreEqual(ReleaseNoteStatuses.Published, publishedOlder.Status);

        var publishedNewer = await PublishAsync(
            client,
            adminToken,
            draft.Id,
            draft.Version,
            cancellationToken);
        Assert.AreEqual(ReleaseNoteStatuses.Published, publishedNewer.Status);

        var myList = await ListMyReleaseNotesAsync(client, adminToken, cancellationToken);
        Assert.IsTrue(myList.Items.Length >= 2);
        Assert.AreEqual("2.0.0", myList.Items[0].VersionLabel);
        Assert.AreEqual("1.0.0", myList.Items[1].VersionLabel);
        Assert.IsFalse(myList.Items[0].IsRead);

        var latestUnread = await GetLatestUnreadAsync(client, adminToken, cancellationToken);
        Assert.IsNotNull(latestUnread);
        Assert.AreEqual(publishedNewer.Id, latestUnread!.Id);

        var marked = await MarkReadAsync(client, adminToken, publishedNewer.Id, cancellationToken);
        Assert.IsTrue(marked.IsRead);
        Assert.IsNotNull(marked.ReadAtUtc);

        var markedAgain = await MarkReadAsync(client, adminToken, publishedNewer.Id, cancellationToken);
        Assert.IsTrue(markedAgain.IsRead);

        var latestAfterRead = await GetLatestUnreadAsync(client, adminToken, cancellationToken);
        Assert.IsNotNull(latestAfterRead);
        Assert.AreEqual(publishedOlder.Id, latestAfterRead!.Id);

        var retracted = await RetractAsync(
            client,
            adminToken,
            publishedOlder.Id,
            publishedOlder.Version,
            cancellationToken);
        Assert.AreEqual(ReleaseNoteStatuses.Retracted, retracted.Status);

        var myListAfterRetract = await ListMyReleaseNotesAsync(client, adminToken, cancellationToken);
        Assert.IsFalse(myListAfterRetract.Items.Any(item => item.Id == publishedOlder.Id));

        var deleteTarget = await CreateDraftAsync(
            client,
            adminToken,
            "3.0.0",
            "待删除草稿",
            "仅用于删除测试",
            cancellationToken);
        using var getDeleteTargetRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/platform/host-release-notes/{deleteTarget.Id:D}");
        getDeleteTargetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getDeleteTargetResponse = await client.SendAsync(getDeleteTargetRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getDeleteTargetResponse.StatusCode);
        var deleteTargetLatest = await getDeleteTargetResponse.Content
            .ReadFromJsonAsync<HostReleaseNoteResponse>(cancellationToken);
        Assert.IsNotNull(deleteTargetLatest);
        using var deleteRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/platform/host-release-notes/{deleteTarget.Id:D}/delete",
            adminToken,
            new DeleteHostReleaseNoteRequest(deleteTargetLatest.Version));
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.NoContent,
            deleteResponse.StatusCode,
            await deleteResponse.Content.ReadAsStringAsync(cancellationToken));

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/platform/host-release-notes/{deleteTarget.Id:D}");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);

        await VerifyInvalidVersionLabelAsync(client, adminToken, cancellationToken);
    }

    private static async Task VerifyInvalidVersionLabelAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/platform/host-release-notes",
            accessToken,
            new CreateHostReleaseNoteRequest("bad-version", "无效版本", "内容"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            PlatformErrorCodes.ReleaseNoteInvalidVersionLabel,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HostReleaseNoteResponse> CreateDraftAsync(
        HttpClient client,
        string accessToken,
        string versionLabel,
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/platform/host-release-notes",
            accessToken,
            new CreateHostReleaseNoteRequest(versionLabel, title, content));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostReleaseNoteResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task<HostReleaseNoteResponse> PublishAsync(
        HttpClient client,
        string accessToken,
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/platform/host-release-notes/{releaseNoteId:D}/publish",
            accessToken,
            new PublishHostReleaseNoteRequest(version));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var published = await response.Content.ReadFromJsonAsync<HostReleaseNoteResponse>(
            cancellationToken);
        Assert.IsNotNull(published);
        return published;
    }

    private static async Task<HostReleaseNoteResponse> RetractAsync(
        HttpClient client,
        string accessToken,
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/platform/host-release-notes/{releaseNoteId:D}/retract",
            accessToken,
            new RetractHostReleaseNoteRequest(version));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var retracted = await response.Content.ReadFromJsonAsync<HostReleaseNoteResponse>(
            cancellationToken);
        Assert.IsNotNull(retracted);
        return retracted;
    }

    private static async Task<PagedMyReleaseNoteResponses> ListMyReleaseNotesAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/platform/my-release-notes?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedMyReleaseNoteResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        return page;
    }

    private static async Task<MyReleaseNoteResponse?> GetLatestUnreadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/platform/my-release-notes/latest-unread");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<MyReleaseNoteResponse>(cancellationToken);
    }

    private static async Task<MyReleaseNoteResponse> MarkReadAsync(
        HttpClient client,
        string accessToken,
        Guid releaseNoteId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/platform/my-release-notes/{releaseNoteId:D}/read");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var marked = await response.Content.ReadFromJsonAsync<MyReleaseNoteResponse>(
            cancellationToken);
        Assert.IsNotNull(marked);
        return marked;
    }

    private sealed record PagedMyReleaseNoteResponses(
        MyReleaseNoteResponse[] Items,
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
