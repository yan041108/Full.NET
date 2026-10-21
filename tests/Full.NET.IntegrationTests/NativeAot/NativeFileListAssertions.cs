using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>复用原生文档上传，验证共享文件列表的时间窗、字面搜索与静态物化。</summary>
internal static class NativeFileListAssertions
{
    internal static async Task VerifyAsync(HttpClient client, string token, Guid fileId,
        CancellationToken cancellationToken)
    {
        var page = await ReadAsync(client, token, Query("native.docx"), cancellationToken);
        Assert.AreEqual(1L, page.Total);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual(fileId, page.Items[0].Id);
        var created = page.Items[0].CreatedAtUtc;

        var explicitWindow = await ReadAsync(client, token,
            Query("native.docx", created.AddMinutes(-1), created.AddMinutes(1)), cancellationToken);
        Assert.AreEqual(1L, explicitWindow.Total);
        Assert.AreEqual(fileId, explicitWindow.Items.Single().Id);
        var beforeCreation = await ReadAsync(client, token,
            Query("native.docx", created.AddMinutes(-2), created.AddMinutes(-1)), cancellationToken);
        Assert.AreEqual(0L, beforeCreation.Total);
        Assert.AreEqual(0, beforeCreation.Items.Count);

        // 这些字面输入若漏转义，会把现成的 native.docx 错当成匹配项。
        foreach (var literal in new[] { "native%.docx", "native_docx", "native[.]docx", "native!.docx" })
        {
            var absent = await ReadAsync(client, token, Query(literal), cancellationToken);
            Assert.AreEqual(0L, absent.Total, literal);
            Assert.AreEqual(0, absent.Items.Count, literal);
        }

        foreach (var invalid in new[]
        {
            Query("native.docx", created.AddMinutes(1), created.AddMinutes(-1)),
            Query("native.docx", created.AddDays(-32), created.AddMinutes(1)),
        })
        {
            using var request = Authorized(token, invalid);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 授权仅放在单次请求上，后续分享创建与轮询仍必须真实匿名。
        Assert.IsFalse(client.DefaultRequestHeaders.Contains("Authorization"));
        using var anonymous = await client.GetAsync(Query("native.docx"), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task<PagedResult<HostFileResponse>> ReadAsync(HttpClient client, string token,
        string uri, CancellationToken cancellationToken)
    {
        using var request = Authorized(token, uri);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var page = await response.Content.ReadFromJsonAsync<PagedResult<HostFileResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        return page;
    }

    private static HttpRequestMessage Authorized(string token, string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string Query(string keyword, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var uri = "/api/v1/files/host-files?page=1&pageSize=1&fileNameContains=" + Uri.EscapeDataString(keyword);
        if (from is { } start)
            uri += "&fromUtc=" + Uri.EscapeDataString(start.ToString("O", CultureInfo.InvariantCulture));
        if (to is { } end)
            uri += "&toUtc=" + Uri.EscapeDataString(end.ToString("O", CultureInfo.InvariantCulture));
        return uri;
    }
}
