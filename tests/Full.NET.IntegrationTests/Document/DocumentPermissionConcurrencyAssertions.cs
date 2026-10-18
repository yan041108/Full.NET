using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.IntegrationTests.Document;

/// <summary>JIT 双库与原生 API 共用的授权快照、空集合和并发覆盖验收。</summary>
internal static class DocumentPermissionConcurrencyAssertions
{
    private const string Path = "/api/v1/document/host/permissions";

    public static async Task<HostDocumentPermissionSnapshotResponse> ReadAsync(
        HttpClient client, string token, Guid documentId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, $"{Path}/snapshot/{documentId:D}", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HostDocumentPermissionSnapshotResponse>(cancellationToken))!;
    }

    public static async Task VerifyAsync(HttpClient client, string token, Guid documentId, CancellationToken cancellationToken)
    {
        using (var anonymous = await client.GetAsync($"{Path}/snapshot/{documentId:D}", cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        using (var missingRequest = Authorized(HttpMethod.Get, $"{Path}/snapshot/{Guid.NewGuid():D}", token))
        using (var missing = await client.SendAsync(missingRequest, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }

        var before = await ReadAsync(client, token, documentId, cancellationToken);
        using (var clear = await WriteAsync(client, token, new(documentId, []) { Version = before.Version }, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, clear.StatusCode);
        }
        var empty = await ReadAsync(client, token, documentId, cancellationToken);
        Assert.AreEqual(before.Version + 1, empty.Version);
        Assert.AreEqual(0, empty.Permissions.Count);

        var firstUser = Guid.CreateVersion7();
        var secondUser = Guid.CreateVersion7();
        var writes = await Task.WhenAll(
            WriteAsync(client, token, new(documentId, [new(firstUser, "read")]) { Version = empty.Version }, cancellationToken),
            WriteAsync(client, token, new(documentId, [new(secondUser, "write")]) { Version = empty.Version }, cancellationToken));
        try
        {
            CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, writes.Select(r => r.StatusCode).ToArray());
        }
        finally
        {
            foreach (var response in writes) response.Dispose();
        }
        var winner = await ReadAsync(client, token, documentId, cancellationToken);
        Assert.AreEqual(empty.Version + 1, winner.Version);
        Assert.AreEqual(1, winner.Permissions.Count);
        Assert.IsTrue(winner.Permissions[0].UserId == firstUser || winner.Permissions[0].UserId == secondUser);

        using (var stale = await WriteAsync(client, token, new(documentId, []) { Version = empty.Version }, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
            using var json = System.Text.Json.JsonDocument.Parse(await stale.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("document.host_permission.version_conflict", json.RootElement.GetProperty("code").GetString());
        }
        using (var unversioned = await WriteAsync(client, token, new(documentId, []), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, unversioned.StatusCode);
        }
        var unchanged = await ReadAsync(client, token, documentId, cancellationToken);
        Assert.AreEqual(winner.Version, unchanged.Version);
        Assert.AreEqual(winner.Permissions[0].UserId, unchanged.Permissions[0].UserId);

        // 同时读取和替换时只能观察旧提交或新提交，不能拼接新修订号与旧授权集合。
        var readTask = ReadAsync(client, token, documentId, cancellationToken);
        using var replacement = await WriteAsync(client, token, new(documentId, []) { Version = winner.Version }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, replacement.StatusCode);
        var racing = await readTask;
        if (racing.Version == winner.Version)
        {
            Assert.AreEqual(1, racing.Permissions.Count);
            Assert.AreEqual(winner.Permissions[0].UserId, racing.Permissions[0].UserId);
        }
        else
        {
            Assert.AreEqual(winner.Version + 1, racing.Version);
            Assert.AreEqual(0, racing.Permissions.Count);
        }
        var final = await ReadAsync(client, token, documentId, cancellationToken);
        Assert.AreEqual(winner.Version + 1, final.Version);
        Assert.AreEqual(0, final.Permissions.Count);
    }

    private static Task<HttpResponseMessage> WriteAsync(HttpClient client, string token,
        SetHostDocumentPermissionsRequest body, CancellationToken cancellationToken) =>
        SendAsync(client, Authorized(HttpMethod.Post, Path, token, body), cancellationToken);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request) return await client.SendAsync(request, cancellationToken);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
