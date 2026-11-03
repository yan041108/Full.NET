using System.Net;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Features.DocumentAccessLogs;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Document;

/// <summary>三批复审补充真实双库边界：撤权、删除和释放消息先于迟到 Claim。</summary>
public sealed partial class DocumentBatchThreeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Review_authenticated_streams_revalidate_revocation_and_deletion(DatabaseProvider provider)
    {
        using var factory = await FactoryAsync(provider, new AdvanceClock());
        using var client = factory.CreateClientForHost("localhost");
        var actor = await factory.CreateHostIdentityAsync($"doc-review-stream-{Guid.NewGuid():N}", Permissions.Concat([
            HostDocumentPermissionManagementPermissions.Set, HostDocumentPermissionManagementPermissions.Read,
            HostDocumentRecycleBinPermissions.Restore]).ToArray());
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        var task = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks,
            new CreateHostDocumentPreviewTaskRequest(item.Id, null), HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var s = scope.ServiceProvider; s.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        Assert.AreEqual(1, await Runner(s, new Converter()).ProcessPendingAsync(CancellationToken.None));
        var queries = s.GetRequiredService<IQueryExecutor>();
        var policy = new DocumentAccessPolicy(queries, new ReviewActor(actor.UserId));
        var reader = new ReviewReader(s.GetRequiredService<IHostFileContentReader>());
        var items = new HostDocumentItemQueryService(s.GetRequiredService<IMultiResultQueryExecutor>(), queries, reader,
            s.GetRequiredService<DocumentAccessLogRecorder>(), s.GetRequiredService<DocumentItemTagAssignmentService>(),
            s.GetRequiredService<IOptions<DatabaseOptions>>(), policy);
        var previews = new HostDocumentPreviewTaskQueryService(queries, s.GetRequiredService<IMultiResultQueryExecutor>(), reader,
            s.GetRequiredService<IOptions<DatabaseOptions>>(), policy, s.GetRequiredService<DocumentAccessLogRecorder>());
        Func<Task<Result<HostFileContent>>>[] paths = [
            () => items.OpenCurrentVersionContentAsync(item.Id),
            () => items.OpenVersionPreviewAsync(item.Id, item.CurrentVersion!.Id),
            () => previews.OpenOutputContentAsync(task.Id)];
        foreach (var deleted in new[] { false, true })
        foreach (var open in paths)
        {
            reader.BeforeReturn = async () =>
            {
                var context = await JsonAsync<HostDocumentPermissionContext>(client, actor.AccessToken, HttpMethod.Get,
                    $"/api/v1/document/host/permissions/context/{item.Id}");
                if (deleted)
                    await JsonAsync<bool>(client, actor.AccessToken, HttpMethod.Post, Items + $"/{item.Id}/delete", new DeleteHostDocumentItemRequest(context.Version));
                else
                    await JsonAsync<HostDocumentPermissionResponse[]>(client, actor.AccessToken, HttpMethod.Post,
                        "/api/v1/document/host/permissions", new SetHostDocumentPermissionsRequest(item.Id, [], "restricted", context.Version));
            };
            var logsBefore = await CountAsync(queries, "fn_document_access_log", "DocumentItemId = @Id", ("Id", item.Id));
            var result = await open();
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(deleted ? ErrorType.NotFound : ErrorType.Forbidden, result.Error!.Type);
            Assert.IsNotNull(reader.LastStream); Assert.IsFalse(reader.LastStream.CanRead, "拒绝移交后必须释放已打开的流。");
            Assert.AreEqual(logsBefore, await CountAsync(queries, "fn_document_access_log", "DocumentItemId = @Id", ("Id", item.Id)));
            var latest = await JsonAsync<HostDocumentPermissionContext>(client, actor.AccessToken, HttpMethod.Get,
                $"/api/v1/document/host/permissions/context/{item.Id}");
            if (deleted)
                item = await JsonAsync<HostDocumentItemResponse>(client, actor.AccessToken, HttpMethod.Post,
                    $"/api/v1/document/host/recycle-bin/{item.Id}/restore", new RestoreHostDocumentItemRequest(latest.Version));
            else
                await JsonAsync<HostDocumentPermissionResponse[]>(client, actor.AccessToken, HttpMethod.Post,
                    "/api/v1/document/host/permissions", new SetHostDocumentPermissionsRequest(item.Id, [], "host", latest.Version));
        }
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Review_recycled_published_document_cannot_create_share(DatabaseProvider provider)
    {
        using var factory = await FactoryAsync(provider, new AdvanceClock());
        using var client = factory.CreateClientForHost("localhost");
        var actor = await factory.CreateHostIdentityAsync($"doc-review-share-{Guid.NewGuid():N}",
            Permissions.Concat([HostDocumentPermissions.Publish, HostDocumentSharePermissions.Create]).ToArray());
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        item = await DocumentLifecycleTestSupport.PublishAsync(client, actor.AccessToken, item, CancellationToken.None);
        await JsonAsync<bool>(client, actor.AccessToken, HttpMethod.Post, Items + $"/{item.Id}/delete", new DeleteHostDocumentItemRequest(item.Version));
        using var denied = await SendAsync(client, actor.AccessToken, HttpMethod.Post, "/api/v1/document/host/shares", new CreateHostDocumentShareRequest(item.Id, 1));
        Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode, await denied.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        Assert.AreEqual(0L, await CountAsync(scope.ServiceProvider.GetRequiredService<IQueryExecutor>(), "fn_document_share", "DocumentId = @Id", ("Id", item.Id)));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Review_late_output_claim_after_release_is_durably_released_again(DatabaseProvider provider)
    {
        using var factory = await FactoryAsync(provider, new AdvanceClock());
        using var client = factory.CreateClientForHost("localhost");
        var actor = await factory.CreateHostIdentityAsync($"doc-review-late-{Guid.NewGuid():N}", Permissions);
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        var created = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks,
            new CreateHostDocumentPreviewTaskRequest(item.Id, null), HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var s = scope.ServiceProvider; s.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var lifecycle = s.GetRequiredService<DocumentPreviewLifecycle>();
        var claimed = (await Runner(s, new Converter()).ClaimAsync(1, CancellationToken.None)).Single();
        var output = (await lifecycle.ReferencesAsync(created.Id, CancellationToken.None)).Single(r => r.Purpose == "output");
        await using var bytes = new MemoryStream(Pdf);
        Assert.IsTrue((await s.GetRequiredService<IHostFileUploadWriter>().UploadIdempotentAsync(DocumentPreviewLifecycle.UploadKey(output.Id),
            item.CreatedByUserId, "late.pdf", "application/pdf", bytes, bytes.Length, reservedFileId: output.FileId)).IsSuccess);
        var serializer = s.GetRequiredService<IIntegrationEventSerializer>();
        var handler = s.GetServices<IIntegrationEventHandler>().Single(h => h.EventType == DocumentFileReferenceEventTypes.Released);
        var payload = serializer.Serialize(new DocumentFileReferenceReleased(output.Id, output.FileId, true));
        var commands = s.GetRequiredService<ICommandExecutor>();
        var transaction = s.GetRequiredService<ICommandTransaction>();
        var claims = s.GetRequiredService<IHostFileReferenceClaimService>();
        var delayedClaims = new ReviewClaims(claims, async () =>
        {
            // 任务已读到 preparing 意图，但跨模块 Claim 尚未开始；此时释放事件先消费且找不到 Claim。
            await transaction.ExecuteAsync(async ct => { await lifecycle.ReleaseReferenceAsync(output, DateTimeOffset.UtcNow.AddSeconds(-1), ct); return true; }, CancellationToken.None);
            await handler.HandleAsync(payload, CancellationToken.None);
            Assert.IsFalse(await claims.HasOpenClaimsAsync(output.FileId));
        });
        var late = new DocumentPreviewLifecycle(s.GetRequiredService<IQueryExecutor>(), commands, transaction, delayedClaims,
            s.GetRequiredService<DocumentMutationGuard>(), s.GetRequiredService<DocumentReferenceReleasePublisher>(), s.GetRequiredService<IClock>(),
            s.GetRequiredService<IOptions<DatabaseOptions>>(), s.GetRequiredService<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>());
        var outboxBefore = await CountAsync(s.GetRequiredService<IQueryExecutor>(), "fn_outbox_message", "MessageType = @Type", ("Type", DocumentFileReferenceEventTypes.Released));
        Assert.IsFalse(await late.CompleteAsync(claimed, output.FileId, CancellationToken.None));
        Assert.IsTrue(await claims.HasOpenClaimsAsync(output.FileId), "迟到引用保持 Pending，仍受 Files 对账保护。");
        Assert.AreEqual(0, delayedClaims.ConfirmCalls, "确认前必须发现已释放意图，避免确认后宕机遗留 Active Claim。");
        Assert.AreEqual(HostFileReferenceClaimStates.Pending, (await claims.ClaimAsync(new(
            HostFileReferenceClaimIdempotencyKeys.DocumentPreview(output.Id), "document", output.Id, output.FileId))).Value!.State);
        Assert.AreEqual(outboxBefore + 2, await CountAsync(s.GetRequiredService<IQueryExecutor>(), "fn_outbox_message", "MessageType = @Type", ("Type", DocumentFileReferenceEventTypes.Released)), "首次释放和迟到补偿必须各自可靠入队。");
        await handler.HandleAsync(payload, CancellationToken.None);
        await handler.HandleAsync(payload, CancellationToken.None);
        Assert.IsFalse(await claims.HasOpenClaimsAsync(output.FileId));
        Assert.IsTrue(await claims.HasOpenClaimsAsync(item.CurrentVersion!.FileId));
        Assert.AreNotEqual("succeeded", (await lifecycle.FindAsync(created.Id, CancellationToken.None))!.StatusKey);
    }

    private sealed class ReviewActor(Guid userId) : IDocumentActorContext { public Guid? UserId => userId; }
    private sealed class ReviewReader(IHostFileContentReader inner) : IHostFileContentReader
    {
        internal Func<Task> BeforeReturn { get; set; } = () => Task.CompletedTask;
        internal Stream? LastStream { get; private set; }
        public async Task<Result<HostFileContent>> OpenReadyContentAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            var result = await inner.OpenReadyContentAsync(fileId, cancellationToken);
            if (!result.IsSuccess) return result;
            LastStream = result.Value!.Content;
            await BeforeReturn();
            return Result<HostFileContent>.Success(new(LastStream, "text/plain", result.Value.OriginalFileName));
        }
    }
    private sealed class ReviewClaims(IHostFileReferenceClaimService inner, Func<Task> beforeClaim) : IHostFileReferenceClaimService
    {
        public async Task<Result<HostFileReferenceClaimResult>> ClaimAsync(HostFileReferenceClaimRequest request, CancellationToken cancellationToken = default)
        { await beforeClaim(); return await inner.ClaimAsync(request, cancellationToken); }
        internal int ConfirmCalls { get; private set; }
        public Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(string key, CancellationToken cancellationToken = default)
        { ConfirmCalls++; return inner.ConfirmAsync(key, cancellationToken); }
        public Task<Result<bool>> ReleaseAsync(string key, CancellationToken cancellationToken = default) => inner.ReleaseAsync(key, cancellationToken);
        public Task<bool> HasOpenClaimsAsync(Guid fileId, CancellationToken cancellationToken = default) => inner.HasOpenClaimsAsync(fileId, cancellationToken);
    }
}
