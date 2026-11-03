using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Document;

/// <summary>第三批双库验收：固定引用、上传后宕机恢复、领取上限、取消与独立删除事实。</summary>
[TestClass]
public sealed partial class DocumentBatchThreeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Uploaded_output_recovers_after_crash_and_stale_owner_cannot_commit(DatabaseProvider provider)
    {
        var clock = new AdvanceClock(); using var factory = await FactoryAsync(provider, clock);
        using var client = factory.CreateClientForHost("localhost");
        var actor = await factory.CreateHostIdentityAsync($"doc-b3-{Guid.NewGuid():N}", Permissions);
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        var created = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks,
            new CreateHostDocumentPreviewTaskRequest(item.Id, null), HttpStatusCode.Created);
        Assert.AreEqual(item.CurrentVersion!.Id, created.VersionId);
        Assert.AreEqual("pending", created.StatusKey);
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var lifecycle = services.GetRequiredService<DocumentPreviewLifecycle>();
        var claims = services.GetRequiredService<IHostFileReferenceClaimService>();
        var references = await lifecycle.ReferencesAsync(created.Id, CancellationToken.None);
        Assert.HasCount(2, references);
        var source = references.Single(r => r.Purpose == "source"); var output = references.Single(r => r.Purpose == "output");
        Assert.AreEqual("active", (await claims.ClaimAsync(new(HostFileReferenceClaimIdempotencyKeys.DocumentPreview(source.Id), "document", source.Id, source.FileId))).Value!.State);
        Assert.AreEqual(HostFileReferenceClaimProbeOutcome.Exists,
            (await services.GetServices<IHostFileReferenceClaimProbe>().Single(p => p.ConsumerModule == "document").ProbeReferenceAsync(output.Id, output.FileId)).Outcome);
        var converter = new Converter(); var runner = Runner(services, converter);
        var stale = (await runner.ClaimAsync(1, CancellationToken.None)).Single();
        var writer = services.GetRequiredService<IHostFileUploadWriter>();
        await using var bytes = new MemoryStream(Pdf);
        var uploaded = await writer.UploadIdempotentAsync(DocumentPreviewLifecycle.UploadKey(output.Id), item.CreatedByUserId,
            "recovery.pdf", "application/pdf", bytes, bytes.Length, reservedFileId: output.FileId);
        Assert.IsTrue(uploaded.IsSuccess);
        Assert.IsFalse((await writer.FindUploadAsync(DocumentPreviewLifecycle.UploadKey(output.Id), Guid.CreateVersion7())).IsSuccess);
        await using var wrongReservation = new MemoryStream(Pdf);
        Assert.IsFalse((await writer.UploadIdempotentAsync(DocumentPreviewLifecycle.UploadKey(output.Id), item.CreatedByUserId,
            "recovery.pdf", "application/pdf", wrongReservation, wrongReservation.Length, reservedFileId: Guid.CreateVersion7())).IsSuccess);
        var liveOptions = services.GetRequiredService<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>().CurrentValue;
        liveOptions.TimeoutSeconds = 5; clock.Advance(TimeSpan.FromSeconds(70));
        Assert.HasCount(0, await runner.ClaimAsync(1, CancellationToken.None), "热缩短配置不能提前回收旧租约。");
        liveOptions.TimeoutSeconds = 120;
        // 模拟 Files ready 已提交、Document 尚未保存 FileId 即进程退出。
        clock.Advance(TimeSpan.FromSeconds(111));
        Assert.AreEqual(1, await runner.ProcessPendingAsync(CancellationToken.None));
        Assert.AreEqual(0, converter.Calls, "恢复既有就绪收据时不应再次转换。");
        var done = (await lifecycle.FindAsync(created.Id, CancellationToken.None))!;
        Assert.AreEqual("succeeded", done.StatusKey, done.ErrorCode); Assert.AreEqual(output.FileId, done.OutputFileId);
        Assert.AreEqual(2, done.AttemptCount);
        Assert.IsFalse(await lifecycle.CompleteAsync(stale, output.FileId, CancellationToken.None));
        Assert.IsTrue(await claims.HasOpenClaimsAsync(output.FileId));
        var query = services.GetRequiredService<IQueryExecutor>();
        Assert.AreEqual(1L, await CountAsync(query, "fn_files_file", "UploadOperationKey = @Key", ("Key", DocumentPreviewLifecycle.UploadKey(output.Id))));
        Assert.AreEqual(2L, await CountAsync(query, "fn_document_preview_attempt", "TaskId = @Id", ("Id", created.Id)));
        using var content = await SendAsync(client, actor.AccessToken, HttpMethod.Get, Tasks + $"/{created.Id}/content");
        Assert.AreEqual(HttpStatusCode.OK, content.StatusCode); CollectionAssert.AreEqual(Pdf, await content.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Cancellation_purge_release_and_deletion_retention_survive_parent_removal(DatabaseProvider provider)
    {
        var clock = new AdvanceClock(); using var factory = await FactoryAsync(provider, clock);
        using var client = factory.CreateClientForHost("localhost");
        var actor = await factory.CreateHostIdentityAsync($"doc-b3-cancel-{Guid.NewGuid():N}", Permissions);
        var reader = await factory.CreateHostIdentityAsync($"doc-b3-reader-{Guid.NewGuid():N}", [HostDocumentPreviewTaskPermissions.Read]);
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        var created = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks,
            new CreateHostDocumentPreviewTaskRequest(item.Id, null), HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var lifecycle = services.GetRequiredService<DocumentPreviewLifecycle>(); var runner = Runner(services, new Converter());
        var claimed = (await runner.ClaimAsync(1, CancellationToken.None)).Single();
        // 永久删除持有根锁期间，另一个实例不能绕过根锁续租；清理标记提交后必须拒绝。
        await using (var competingScope = factory.Services.CreateAsyncScope())
        {
            var competitor = competingScope.ServiceProvider; competitor.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var continueDelete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hold = services.GetRequiredService<ICommandTransaction>().ExecuteAsync(async ct =>
            {
                var lockedItem = (await services.GetRequiredService<DocumentMutationGuard>().LockAsync(item.Id, ct))!;
                locked.SetResult(); await continueDelete.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                // 清理只允许从回收站开始；模拟同事务提交软删与清理标记，并断言种子确实生效。
                Assert.AreEqual(1, await services.GetRequiredService<ICommandExecutor>().ExecuteAsync(DocumentItemSql.SoftDelete,
                    Args(("Id", item.Id), ("DeletedAtUtc", clock.UtcNow), ("DeletedByUserId", actor.UserId), ("Version", lockedItem.Version)), ct));
                Assert.AreEqual(1, await services.GetRequiredService<ICommandExecutor>().ExecuteAsync(DocumentItemSql.MarkPurging, Args(("Id", item.Id)), ct));
                return true;
            }, CancellationToken.None);
            await locked.Task;
            var renewal = competitor.GetRequiredService<DocumentPreviewLifecycle>().RenewAsync(claimed, clock.UtcNow, 120, CancellationToken.None);
            try { await Task.Delay(100); Assert.IsFalse(renewal.IsCompleted, "续租必须等待文档根锁。"); }
            finally { continueDelete.TrySetResult(); await Task.WhenAll(hold, renewal).WaitAsync(TimeSpan.FromSeconds(5)); }
            Assert.IsFalse(await renewal.WaitAsync(TimeSpan.FromSeconds(5)), "清理标记提交后不再续租。");
            await services.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.preview.reset_purging", "UPDATE fn_document_item SET IsPurging = 0, IsDeleted = 0, DeletedAtUtc = NULL, DeletedByUserId = NULL WHERE Id = @Id", SqlDataScope.HostOnly), Args(("Id", item.Id)));
        }
        using (var denied = await SendAsync(client, reader.AccessToken, HttpMethod.Post, Tasks + $"/{created.Id}/cancel", new CancelDocumentPreviewTaskRequest(claimed.Version)))
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var conflict = await SendAsync(client, actor.AccessToken, HttpMethod.Post, Tasks + $"/{created.Id}/cancel", new CancelDocumentPreviewTaskRequest(created.Version)))
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks + $"/{created.Id}/cancel", new CancelDocumentPreviewTaskRequest(claimed.Version));
        var references = await lifecycle.ReferencesAsync(created.Id, CancellationToken.None);
        Assert.IsTrue(references.All(r => r.StateKey == "release_pending"));
        var output = references.Single(r => r.Purpose == "output");
        await using var bytes = new MemoryStream(Pdf);
        var upload = await services.GetRequiredService<IHostFileUploadWriter>().UploadIdempotentAsync(DocumentPreviewLifecycle.UploadKey(output.Id),
            item.CreatedByUserId, "late.pdf", "application/pdf", bytes, bytes.Length, reservedFileId: output.FileId);
        Assert.IsTrue(upload.IsSuccess);
        Assert.IsFalse(await lifecycle.CompleteAsync(claimed, output.FileId, CancellationToken.None));
        var retry = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks + $"/{created.Id}/retry");
        Assert.AreNotEqual(created.Id, retry.Id); Assert.AreEqual(0, retry.AttemptCount);
        var deterministic = new Converter { ErrorCode = DocumentErrorCodes.OfficePreviewUnsupportedSource };
        Assert.AreEqual(1, await Runner(services, deterministic).ProcessPendingAsync(CancellationToken.None));
        var failedRetry = (await lifecycle.FindAsync(retry.Id, CancellationToken.None))!;
        Assert.AreEqual("failed", failedRetry.StatusKey); Assert.AreEqual(1, failedRetry.AttemptCount);
        Assert.AreEqual(0, await Runner(services, deterministic).ProcessPendingAsync(CancellationToken.None));
        var retention = services.GetServices<IHostFileRetentionContributor>().Single(x => x.GetType().Name == "DocumentHostFileRetentionContributor");
        Assert.IsTrue(await retention.IsFileReferencedAsync(output.FileId), "有效租约内即使取消也要保留迟到上传。");
        item = await DocumentLifecycleTestSupport.ReadAsync(client, actor.AccessToken, item.Id, CancellationToken.None);
        await JsonAsync<bool>(client, actor.AccessToken, HttpMethod.Post, Items + $"/{item.Id}/delete", new DeleteHostDocumentItemRequest(item.Version));
        await JsonAsync<bool>(client, actor.AccessToken, HttpMethod.Post, $"/api/v1/document/host/recycle-bin/{item.Id}/purge");
        var query = services.GetRequiredService<IQueryExecutor>();
        Assert.AreEqual(0L, await CountAsync(query, "fn_document_preview_task", "DocumentItemId = @Id", ("Id", item.Id)));
        Assert.AreEqual(1L, await CountAsync(query, "fn_document_deletion_fact", "DocumentId = @Id AND VersionCount = 1 AND PreviewTaskCount = 2 AND ActorUserId = @Actor", ("Id", item.Id), ("Actor", actor.UserId)));
        Assert.IsTrue(await retention.IsFileReferencedAsync(output.FileId));
        var handler = services.GetServices<IIntegrationEventHandler>().Single(x => x.EventType == DocumentFileReferenceEventTypes.Released);
        var serializer = services.GetRequiredService<IIntegrationEventSerializer>();
        // 提前交付不消耗失败重试预算，到期维护独立于父任务存在。
        await handler.HandleAsync(serializer.Serialize(new DocumentFileReferenceReleased(references[0].Id, references[0].FileId, true, clock.UtcNow.AddMinutes(3))), CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(181)); await lifecycle.MaintainAsync(CancellationToken.None);
        foreach (var reference in references)
        {
            var payload = serializer.Serialize(new DocumentFileReferenceReleased(reference.Id, reference.FileId, true));
            await handler.HandleAsync(payload, CancellationToken.None); await handler.HandleAsync(payload, CancellationToken.None);
        }
        Assert.IsFalse(await retention.IsFileReferencedAsync(output.FileId));
        Assert.IsTrue(await services.GetRequiredService<IHostFileReferenceClaimService>().HasOpenClaimsAsync(item.CurrentVersion!.FileId), "预览释放不影响原版本 Claim。");
        clock.Advance(TimeSpan.FromDays(89)); await lifecycle.MaintainAsync(CancellationToken.None);
        Assert.AreEqual(1L, await CountAsync(query, "fn_document_deletion_fact", "DocumentId = @Id", ("Id", item.Id)));
        clock.Advance(TimeSpan.FromDays(2)); await lifecycle.MaintainAsync(CancellationToken.None);
        Assert.AreEqual(0L, await CountAsync(query, "fn_document_deletion_fact", "DocumentId = @Id", ("Id", item.Id)));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Crash_reclaims_are_bounded_and_preparing_tasks_are_not_claimable(DatabaseProvider provider)
    {
        var clock = new AdvanceClock(); using var factory = await FactoryAsync(provider, clock);
        using var client = factory.CreateClientForHost("localhost"); var actor = await factory.CreateHostIdentityAsync($"doc-b3-max-{Guid.NewGuid():N}", Permissions);
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        var created = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks, new CreateHostDocumentPreviewTaskRequest(item.Id, null), HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider; services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var commands = services.GetRequiredService<ICommandExecutor>(); var lifecycle = services.GetRequiredService<DocumentPreviewLifecycle>(); var runner = Runner(services, new Converter());
        await commands.ExecuteAsync(new SqlStatement("test.preview.preparing", "UPDATE fn_document_preview_task SET StatusKey='preparing' WHERE Id=@Id", SqlDataScope.HostOnly), Args(("Id", created.Id)));
        Assert.HasCount(0, await runner.ClaimAsync(1, CancellationToken.None));
        await lifecycle.PrepareAsync((await lifecycle.FindAsync(created.Id, CancellationToken.None))!, CancellationToken.None);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var claimed = (await runner.ClaimAsync(1, CancellationToken.None)).Single(); Assert.AreEqual(attempt, claimed.AttemptCount);
            clock.Advance(TimeSpan.FromSeconds(181));
        }
        var exhausted = (await runner.ClaimAsync(1, CancellationToken.None)).Single();
        Assert.AreEqual("failed", exhausted.StatusKey); Assert.AreEqual(3, exhausted.AttemptCount); Assert.AreEqual("document.preview.attempts_exhausted", exhausted.ErrorCode);
        Assert.HasCount(0, await runner.ClaimAsync(1, CancellationToken.None));
        Assert.AreEqual(3L, await CountAsync(services.GetRequiredService<IQueryExecutor>(), "fn_document_preview_attempt", "TaskId = @Id", ("Id", created.Id)));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Source_claim_failure_blocks_execution_and_pending_confirmation_remains_probeable(DatabaseProvider provider)
    {
        var clock = new AdvanceClock(); var gate = new ClaimGate(); using var factory = await FactoryAsync(provider, clock, gate: gate);
        using var client = factory.CreateClientForHost("localhost"); var actor = await factory.CreateHostIdentityAsync($"doc-b3-claim-{Guid.NewGuid():N}", Permissions);
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        var rejected = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks,
            new CreateHostDocumentPreviewTaskRequest(item.Id, null), HttpStatusCode.Created);
        Assert.AreEqual("failed", rejected.StatusKey);
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider; services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        Assert.HasCount(0, await Runner(services, new Converter()).ClaimAsync(10, CancellationToken.None));
        gate.BlockClaim = false;
        var pending = await JsonAsync<HostDocumentPreviewTaskResponse>(client, actor.AccessToken, HttpMethod.Post, Tasks,
            new CreateHostDocumentPreviewTaskRequest(item.Id, null), HttpStatusCode.Created);
        Assert.AreEqual("pending", pending.StatusKey);
        var source = (await services.GetRequiredService<DocumentPreviewLifecycle>().ReferencesAsync(pending.Id, CancellationToken.None)).Single(r => r.Purpose == "source");
        var key = HostFileReferenceClaimIdempotencyKeys.DocumentPreview(source.Id);
        var actualClaims = services.GetRequiredService<IHostFileReferenceClaimService>();
        Assert.AreEqual("pending", (await actualClaims.ClaimAsync(new(key, "document", source.Id, source.FileId))).Value!.State);
        Assert.AreEqual(HostFileReferenceClaimProbeOutcome.Exists, (await services.GetServices<IHostFileReferenceClaimProbe>().Single(p => p.ConsumerModule == "document").ProbeReferenceAsync(source.Id, source.FileId)).Outcome);
        Assert.IsTrue((await actualClaims.ConfirmAsync(key)).IsSuccess);
        Assert.HasCount(1, await Runner(services, new Converter()).ClaimAsync(10, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Disabled_provider_refuses_new_tasks_without_queue_growth(DatabaseProvider provider)
    {
        var clock = new AdvanceClock(); using var factory = await FactoryAsync(provider, clock, false);
        using var client = factory.CreateClientForHost("localhost"); var actor = await factory.CreateHostIdentityAsync($"doc-b3-off-{Guid.NewGuid():N}", Permissions);
        var item = await CreateOfficeAsync(client, actor.AccessToken);
        var capabilities = await JsonAsync<DocumentPreviewCapabilities>(client, null, HttpMethod.Get, "/api/v1/document/public/preview-capabilities");
        Assert.IsFalse(capabilities.Enabled); Assert.IsTrue(capabilities.AllowedMimeTypes.Contains("application/vnd.ms-excel"));
        using var response = await SendAsync(client, actor.AccessToken, HttpMethod.Post, Tasks, new CreateHostDocumentPreviewTaskRequest(item.Id, null));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        Assert.AreEqual(0L, await CountAsync(scope.ServiceProvider.GetRequiredService<IQueryExecutor>(), "fn_document_preview_task", "1=1"));
    }

    private const string Tasks = "/api/v1/document/host/preview-tasks";
    private const string Items = "/api/v1/document/host/items";
    private static readonly byte[] Pdf = "%PDF-1.7\nrecovery\n%%EOF"u8.ToArray();
    private static readonly string[] Permissions = [HostDocumentPermissions.Read, HostDocumentPermissions.Create, HostDocumentPermissions.AddVersion,
        HostDocumentPermissions.Delete, HostDocumentRecycleBinPermissions.Purge, HostDocumentPreviewTaskPermissions.Read,
        HostDocumentPreviewTaskPermissions.Create, HostDocumentPreviewTaskPermissions.Cancel, HostDocumentPreviewTaskPermissions.Retry];
    private static async Task<FullNetApiFactory> FactoryAsync(DatabaseProvider provider, AdvanceClock clock, bool enabled = true, ClaimGate? gate = null)
    {
        var factory = new FullNetApiFactory(provider, provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?> { ["Document:OfficePreviewConversion:Enabled"] = enabled.ToString(), ["Document:OfficePreviewConversion:ProviderKey"] = "external_http",
                ["Document:OfficePreviewConversion:ExternalHttp:BaseUrl"] = "https://example.test/convert" },
            configureTestServices: s => {
                s.RemoveAll<IClock>(); s.AddSingleton<IClock>(clock);
                if (gate is not null) { s.RemoveAll<DocumentPreviewLifecycle>(); s.AddScoped(sp => new DocumentPreviewLifecycle(
                    sp.GetRequiredService<IQueryExecutor>(), sp.GetRequiredService<ICommandExecutor>(), sp.GetRequiredService<ICommandTransaction>(),
                    new GatedClaims(sp.GetRequiredService<IHostFileReferenceClaimService>(), gate), sp.GetRequiredService<DocumentMutationGuard>(),
                    sp.GetRequiredService<DocumentReferenceReleasePublisher>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<IOptions<DatabaseOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>())); }
            });
        await factory.InitializeAsync(); return factory;
    }
    private static DocumentPreviewTaskRunner Runner(IServiceProvider s, Converter converter) => new(s.GetRequiredService<IQueryExecutor>(), s.GetRequiredService<ICommandExecutor>(),
        s.GetRequiredService<ICommandTransaction>(), s.GetRequiredService<IHostFileContentReader>(), s.GetRequiredService<IHostFileUploadWriter>(),
        new DocumentOfficePreviewConversionProviderResolver([converter], new Monitor(s.GetRequiredService<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>().CurrentValue)),
        s.GetRequiredService<IClock>(), s.GetRequiredService<IOptions<DatabaseOptions>>(), s.GetRequiredService<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>(),
        s.GetRequiredService<DocumentPreviewLifecycle>(), s.GetRequiredService<IIdGenerator>());
    private static async Task<HostDocumentItemResponse> CreateOfficeAsync(HttpClient client, string token)
    {
        var item = await JsonAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items, new CreateHostDocumentItemRequest("Office recovery", null), HttpStatusCode.Created);
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent("office-source"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/msword"); form.Add(file, "file", "source.doc"); form.Add(new StringContent(Guid.CreateVersion7().ToString()), "requestKey");
        using var request = new HttpRequestMessage(HttpMethod.Post, Items + $"/{item.Id}/versions/upload"); request.Headers.Authorization = new("Bearer", token); request.Content = form;
        using var response = await client.SendAsync(request); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>())!;
    }
    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] args) => args.ToDictionary(x => x.Key, x => x.Value);
    private static Task<long> CountAsync(IQueryExecutor queries, string table, string predicate, params (string Key, object? Value)[] args) =>
        queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("test.preview.count", $"SELECT COUNT(1) FROM {table} WHERE {predicate}", SqlDataScope.HostOnly), Args(args));
    private static async Task<T> JsonAsync<T>(HttpClient client, string? token, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(client, token, method, path, body); Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string? token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path); if (token is not null) request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body); return await client.SendAsync(request);
    }
    private sealed class AdvanceClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        internal void Advance(TimeSpan amount) => UtcNow += amount;
    }
    private sealed class Converter : IDocumentOfficePreviewConversionProvider
    {
        public string ProviderKey => "external_http"; internal int Calls { get; private set; }
        internal string? ErrorCode { get; init; }
        public Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(DocumentOfficePreviewConversionInput input, string workingDirectory, CancellationToken cancellationToken = default)
        {
            Calls++; return Task.FromResult(ErrorCode is null ? Result<DocumentOfficePreviewConversionOutput>.Success(new(new MemoryStream(Pdf), Pdf.Length))
                : Result<DocumentOfficePreviewConversionOutput>.Failure(new Error(ErrorCode, "Controlled converter failure.", ErrorType.BusinessRule)));
        }
    }
    private sealed class ClaimGate { internal bool BlockClaim { get; set; } = true; }
    private sealed class GatedClaims(IHostFileReferenceClaimService inner, ClaimGate gate) : IHostFileReferenceClaimService
    {
        public Task<Result<HostFileReferenceClaimResult>> ClaimAsync(HostFileReferenceClaimRequest request, CancellationToken cancellationToken = default) =>
            gate.BlockClaim ? Task.FromResult(Result<HostFileReferenceClaimResult>.Failure(new Error("test.claim_unavailable", "Controlled source claim failure.", ErrorType.BusinessRule)))
                : inner.ClaimAsync(request, cancellationToken);
        public Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<HostFileReferenceClaimResult>.Failure(new Error("test.confirm_unavailable", "Controlled confirmation failure.", ErrorType.BusinessRule)));
        public Task<Result<bool>> ReleaseAsync(string idempotencyKey, CancellationToken cancellationToken = default) => inner.ReleaseAsync(idempotencyKey, cancellationToken);
        public Task<bool> HasOpenClaimsAsync(Guid fileId, CancellationToken cancellationToken = default) => inner.HasOpenClaimsAsync(fileId, cancellationToken);
    }
    private sealed class Monitor(DocumentOfficePreviewConversionOptions options) : IOptionsMonitor<DocumentOfficePreviewConversionOptions>
    {
        public DocumentOfficePreviewConversionOptions CurrentValue => options;
        public DocumentOfficePreviewConversionOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<DocumentOfficePreviewConversionOptions, string?> listener) => null;
    }
}
