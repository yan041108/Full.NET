using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.IntegrationTests.Document;

/// <summary>旧场景夹具显式准备就绪发布文档，避免将创建默认状态与分享前置条件混在一起。</summary>
internal static class DocumentLifecycleTestSupport
{
    internal static async Task<HostDocumentItemResponse> ReadAsync(HttpClient client, string token, Guid id, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, $"/api/v1/document/host/items/{id}", token);
        using var response = await client.SendAsync(request, ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(ct))!;
    }

    internal static async Task<HostDocumentItemResponse> PublishAsync(HttpClient client, string token, HostDocumentItemResponse item, CancellationToken ct)
    {
        if (item.CurrentVersion is null)
        {
            using var form = new MultipartFormDataContent();
            var content = new ByteArrayContent("document-fixture"u8.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(content, "file", "fixture.txt");
            form.Add(new StringContent(Guid.CreateVersion7().ToString("D")), "requestKey");
            using var upload = Authorized(HttpMethod.Post, $"/api/v1/document/host/items/{item.Id}/versions/upload", token);
            upload.Content = form;
            using var response = await client.SendAsync(upload, ct);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            item = (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(ct))!;
        }
        if (item.Status == HostDocumentStatus.Draft)
        {
            using var publish = Authorized(HttpMethod.Post, $"/api/v1/document/host/items/{item.Id}/publish", token);
            publish.Content = JsonContent.Create(new ChangeHostDocumentStatusRequest(item.Version));
            using var response = await client.SendAsync(publish, ct);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            item = (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(ct))!;
        }
        return item;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
