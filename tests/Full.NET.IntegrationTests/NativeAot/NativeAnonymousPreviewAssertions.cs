using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生 HTTP 真实写入并读取匿名任务的 NULL 发起人，覆盖凭据重试与静态物化闭包。</summary>
internal static class NativeAnonymousPreviewAssertions
{
    // 只验任务受理/轮询及 NULL 发起人；API 不执行转换，保留的回环端点也不代表转换器验收通过。
    internal static IReadOnlyDictionary<string, string?> TaskQueueSettings { get; } =
        new Dictionary<string, string?>
        {
            ["Document:OfficePreviewConversion:Enabled"] = "true",
            ["Document:OfficePreviewConversion:ProviderKey"] = "external_http",
            ["Document:OfficePreviewConversion:ExternalHttp:BaseUrl"] = "http://127.0.0.1:1/native-task-contract-only"
        };

    internal static async Task VerifyAsync(HttpClient client, string token, Guid documentId, string shareCode,
        CancellationToken cancellationToken)
    {
        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent("native-preview-input"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        upload.Add(file, "file", "native.docx");
        upload.Add(new StringContent(Guid.CreateVersion7().ToString("D")), "requestKey");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/document/host/items/{documentId:D}/versions/upload")
            { Content = upload };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var uploaded = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync(cancellationToken));
        var item = await uploaded.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(item);
        Assert.IsNotNull(item.CurrentVersion);
        await NativeFileListAssertions.VerifyAsync(client, token, item.CurrentVersion.FileId, cancellationToken);
        var path = $"/api/v1/document/public/shares/{shareCode}";
        using var accessResponse = await client.PostAsJsonAsync(path + "/access", new AccessHostDocumentShareRequest(null) { RequestKey = Guid.CreateVersion7() }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, accessResponse.StatusCode, await accessResponse.Content.ReadAsStringAsync(cancellationToken));
        var access = await accessResponse.Content.ReadFromJsonAsync<HostDocumentShareAccessResponse>(cancellationToken);
        Assert.IsNotNull(access);
        var creation = new AccessHostDocumentShareRequest { PreviewGrant = access.PreviewGrant };
        HostDocumentPreviewTaskResponse? first = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsJsonAsync(path + "/preview-task", creation, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            var task = await response.Content.ReadFromJsonAsync<HostDocumentPreviewTaskResponse>(cancellationToken);
            Assert.IsNotNull(task);
            Assert.IsNull(task.RequestedByUserId);
            Assert.AreEqual(documentId, task.DocumentItemId);
            Assert.IsFalse(string.IsNullOrEmpty(task.PreviewGrant));
            if (first is not null)
            {
                Assert.AreEqual(first.Id, task.Id);
                Assert.AreEqual(first.PreviewPollUntilUtc, task.PreviewPollUntilUtc);
            }
            first = task;
        }
        Assert.IsNotNull(first);
        using var poll = await client.PostAsJsonAsync(path + $"/preview-tasks/{first.Id:D}",
            new AccessHostDocumentShareRequest { PreviewGrant = first.PreviewGrant }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, poll.StatusCode, await poll.Content.ReadAsStringAsync(cancellationToken));
        var loaded = await poll.Content.ReadFromJsonAsync<HostDocumentPreviewTaskResponse>(cancellationToken);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(first.Id, loaded.Id);
        Assert.IsNull(loaded.RequestedByUserId);
    }
}
