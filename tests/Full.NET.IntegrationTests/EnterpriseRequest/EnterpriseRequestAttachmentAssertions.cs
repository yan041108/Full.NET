using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    // 复用双库 CRUD 数据库，实际验证字节下载、精确归属及版本竞争，不再启动一组 API fixture。
    private static async Task VerifyAttachmentsAsync(FullNetApiFactory factory, HttpClient client, string token,
        EnterpriseRequestResponse parent, CancellationToken ct)
    {
        var path = $"{BasePath}/{parent.Id:D}/attachments";
        using var initial = await Send(HttpMethod.Get, path);
        Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode, await initial.Content.ReadAsStringAsync(ct));
        using var initialJson = JsonDocument.Parse(await initial.Content.ReadAsStringAsync(ct));
        var version = long.Parse(initialJson.RootElement.GetProperty("requestVersion").GetString()!);
        Assert.AreEqual(0, initialJson.RootElement.GetProperty("items").GetArrayLength());
        using var uploaded = await Upload(version);
        Assert.AreEqual(HttpStatusCode.OK, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync(ct));
        using var uploadedJson = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync(ct));
        var attachment = uploadedJson.RootElement.GetProperty("attachment");
        var attachmentId = attachment.GetProperty("id").GetGuid();
        var fileId = attachment.GetProperty("fileId").GetGuid();
        Assert.AreEqual("probe.html", attachment.GetProperty("originalFileName").GetString());
        Assert.AreEqual(version + 1, long.Parse(uploadedJson.RootElement.GetProperty("requestVersion").GetString()!));
        using var content = await Send(HttpMethod.Get, $"{path}/{attachmentId:D}/content");
        Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
        Assert.AreEqual("<script>probe</script>", await content.Content.ReadAsStringAsync(ct));
        Assert.AreEqual("application/octet-stream", content.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("attachment", content.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual("nosniff", content.Headers.GetValues("X-Content-Type-Options").Single());
        using var wrongRequest = await Send(HttpMethod.Get, $"{BasePath}/{Guid.NewGuid():D}/attachments/{attachmentId:D}/content");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongRequest.StatusCode);
        using var wrongId = await Send(HttpMethod.Get, $"{path}/{fileId:D}/content");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongId.StatusCode, "文件标识不能替代业务附件标识。");
        using var stale = await Upload(version);
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        factory.Services.GetRequiredService<LineInsertFailureProbe>().AttachmentConflict = true;
        using var bindingConflict = await Upload(version + 1);
        Assert.AreEqual(HttpStatusCode.Conflict, bindingConflict.StatusCode);
        factory.Services.GetRequiredService<LineInsertFailureProbe>().ExpireAttachment = true;
        using var expiryConflict = await Upload(version + 1);
        Assert.AreEqual(HttpStatusCode.Conflict, expiryConflict.StatusCode, "后台过期撤销先成功时，前台不能交付已清理的附件。");
        factory.Services.GetRequiredService<LineInsertFailureProbe>().AttachmentInsertFailure = true;
        using var bindingFailure = await Upload(version + 1);
        Assert.AreEqual(HttpStatusCode.InternalServerError, bindingFailure.StatusCode);
        using var preserved = await Send(HttpMethod.Get, path);
        using var preservedJson = JsonDocument.Parse(await preserved.Content.ReadAsStringAsync(ct));
        Assert.AreEqual(1, preservedJson.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(attachmentId, preservedJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.AreEqual(version + 1, long.Parse(preservedJson.RootElement.GetProperty("requestVersion").GetString()!));
        using var staleDelete = await Send(HttpMethod.Delete, $"{path}/{attachmentId:D}", new { version = version.ToString() });
        Assert.AreEqual(HttpStatusCode.Conflict, staleDelete.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Tenancy.ICurrentTenantContextWriter>();
        tenant.SetTenant(new(parent.TenantId, "acme", "Acme"));
        var owner = scope.ServiceProvider.GetServices<ITenantResourceFileOwner>().Single(value => value.OwnerModuleKey == "enterprise_request");
        Assert.IsTrue(await owner.IsReferencedAsync(parent.Id, fileId, ct));
        Assert.IsFalse(await owner.IsReferencedAsync(Guid.NewGuid(), fileId, ct));
        var fileStore = scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>();
        var readyFiles = await fileStore.ListReadyAsync("enterprise_request", parent.Id, ct);
        Assert.AreEqual(2, readyFiles.Count, "已确认冲突的新文件应被释放，未知异常留下的文件等待精确 owner 对账。");
        var pendingFileId = readyFiles.Single(value => value.FileId != fileId).FileId;
        Assert.IsTrue(await owner.IsReferencedAsync(parent.Id, pendingFileId, ct), "未到过期时间的上传意图保持可恢复。");
        // 回拨测试租期后由生产 owner 执行过期 CAS；活动绑定不受影响。
        var commands = scope.ServiceProvider.GetRequiredService<Full.NET.Data.Abstractions.ICommandExecutor>();
        await commands.ExecuteAsync(BackdateAttachmentUploadLease,
            new Dictionary<string, object?> { ["RequestId"] = parent.Id, ["FileId"] = pendingFileId, ["Now"] = DateTimeOffset.UtcNow.AddHours(-1) }, ct);
        Assert.IsFalse(await owner.IsReferencedAsync(parent.Id, pendingFileId, ct));
        Assert.IsTrue(await owner.IsReferencedAsync(parent.Id, fileId, ct));
        using var removed = await Send(HttpMethod.Delete, $"{path}/{attachmentId:D}", new { version = (version + 1).ToString() });
        Assert.AreEqual(HttpStatusCode.OK, removed.StatusCode, await removed.Content.ReadAsStringAsync(ct));
        Assert.IsFalse(await owner.IsReferencedAsync(parent.Id, fileId, ct));
        using var deletedContent = await Send(HttpMethod.Get, $"{path}/{attachmentId:D}/content");
        Assert.AreEqual(HttpStatusCode.NotFound, deletedContent.StatusCode);
        using var after = await Send(HttpMethod.Get, path);
        using var finalJson = JsonDocument.Parse(await after.Content.ReadAsStringAsync(ct));
        Assert.AreEqual(0, finalJson.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(version + 2, long.Parse(finalJson.RootElement.GetProperty("requestVersion").GetString()!));
        tenant.Clear();

        async Task<HttpResponseMessage> Upload(long expectedVersion)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(expectedVersion.ToString()), "version");
            var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes("<script>probe</script>"));
            bytes.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            form.Add(bytes, "file", "../probe.html"); request.Content = form;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, ct);
        }
        async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            return await client.SendAsync(request, ct);
        }
    }
}
