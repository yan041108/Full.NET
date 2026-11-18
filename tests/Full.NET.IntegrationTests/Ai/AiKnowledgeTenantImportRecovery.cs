using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>配额提交后响应丢失，通过正式对账器恢复时不能删除未绑定来源或重复计量。</summary>
internal sealed class AiKnowledgeTenantImportRecovery : IDisposable
{
    internal string FilesRoot { get; } = Path.Combine(Path.GetTempPath(), "fullnet-ai-recovery-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (Directory.Exists(FilesRoot)) Directory.Delete(FilesRoot, recursive: true);
    }

    internal void Configure(IServiceCollection services)
    {
        var descriptor = services.Last(item => item.ServiceType == typeof(ITenantFileStorageQuotaPort));
        services.RemoveAll<ITenantFileStorageQuotaPort>();
        services.AddScoped<ITenantFileStorageQuotaPort>(provider => new LostConfirmation(
            (ITenantFileStorageQuotaPort)(descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), this));
        var files = services.Last(item => item.ServiceType == typeof(ITenantResourceFileStore));
        services.RemoveAll<ITenantResourceFileStore>();
        services.AddScoped<ITenantResourceFileStore>(provider => new PausedFiles(
            (ITenantResourceFileStore)(files.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, files.ImplementationType!)), this));
    }
    private int _confirmationLost;
    private int _pauseNextList;
    private int _trackingUploads;
    private int _uploadsDuringPause;
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _unavailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiKnowledgeTenantImportRecovery faults)
    {
        await factory.InitializeAsync();
        var owner = await factory.CreateHostIdentityAsync($"tenant-recovery-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create,
             AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.ImportsRetry, AiKnowledgeDocumentPermissions.SourceRead,
             AiKnowledgeDocumentPermissions.VersionsRead, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id));
        using var createBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("恢复来源", null));
        var knowledgeBase = (await createBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var documents = $"/api/v1/ai/knowledge-bases/{knowledgeBase.Id}/documents";
        using var createDocument = await client.PostAsJsonAsync(documents, new CreateAiKnowledgeDocumentRequest("恢复文件", null));
        var document = (await createDocument.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var path = documents + $"/{document.Id}";
        var requestKey = Guid.NewGuid();
        const string text = "配额仅计量一次";
        using var lost = await client.PostAsync(path + $"/versions?version=1&requestKey={requestKey}&fileName=source.txt", new StringContent(text, Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.InternalServerError, lost.StatusCode);
        Guid versionId;
        Guid fileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
            writer.SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
            var query = services.GetRequiredService<IQueryExecutor>();
            var projection = new SqlStatement("test.ai.recovery_version", "SELECT operation.VersionId FROM fn_ai_knowledge_import AS operation INNER JOIN fn_ai_knowledge_document AS document ON document.Id=operation.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE document.Id=@DocumentId AND knowledge.TenantId=@TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
            versionId = await query.QuerySingleOrDefaultAsync<Guid>(projection, new Dictionary<string, object?> { ["DocumentId"] = document.Id });
            var files = services.GetRequiredService<ITenantResourceFileStore>();
            Assert.HasCount(0, await files.ListReadyAsync("ai", versionId));
            var runner = new PendingTenantResourceFileReconciliationRunner(query, services.GetRequiredService<ICommandExecutor>(),
                services.GetRequiredService<FileStorageProviderRegistry>(), services.GetServices<ITenantResourceFileOwner>(),
                services.GetRequiredService<IActiveTenantContextResolver>(), writer, services.GetRequiredService<IOptions<DatabaseOptions>>(),
                new LaterClock(), services.GetRequiredService<ITenantFileStorageQuotaPort>());
            var options = new PendingTenantResourceFileReconciliationOptions { Enabled = true, MinimumAgeSeconds = 30 };
            Assert.AreEqual(1, (await runner.RunOnceAsync(options, CancellationToken.None)).Promoted);
            writer.SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
            fileId = (await files.ListReadyAsync("ai", versionId)).Single().FileId;
            Assert.AreEqual(0, (await runner.RunOnceAsync(options, CancellationToken.None)).Released);
            writer.SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
            Assert.AreEqual(fileId, (await files.ListReadyAsync("ai", versionId)).Single().FileId);
        }
        using var management = factory.CreateClientForHost("localhost");
        management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateHostAccessTokenAsync(
            ["tenancy.host_tenants.read", "tenancy.tenant_lifecycle.suspend", "tenancy.tenant_lifecycle.reactivate"]));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var current = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            current.SetHost();
            var identifier = "ai-spare-" + Guid.NewGuid().ToString("N")[..10];
            var spare = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest(identifier, "AI spare", identifier + ".localhost"));
            Assert.IsTrue(spare.IsSuccess, spare.Error?.Code);
        }
        using var currentResponse = await management.GetAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}");
        Assert.AreEqual(HttpStatusCode.OK, currentResponse.StatusCode);
        var currentTenant = await currentResponse.Content.ReadFromJsonAsync<TenantSummary>();
        Assert.IsNotNull(currentTenant);
        Volatile.Write(ref faults._pauseNextList, 1);
        Volatile.Write(ref faults._trackingUploads, 1);
        var paused = client.PostAsync(path + $"/versions?version=1&requestKey={requestKey}&fileName=source.txt",
            new StringContent(text, Encoding.UTF8, "text/plain"));
        TenantSummary? suspended = null;
        try
        {
            await faults._entered.Task.WaitAsync(TimeSpan.FromSeconds(40));
            using var response = await management.PostAsJsonAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}/suspend",
                new SuspendTenantRequest(currentTenant.Version));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            suspended = await response.Content.ReadFromJsonAsync<TenantSummary>();
            Assert.IsNotNull(suspended);
            Assert.IsFalse(suspended.IsActive);
            faults._release.TrySetResult();
            await faults._unavailable.Task.WaitAsync(TimeSpan.FromSeconds(40));
            using var unavailable = await paused.WaitAsync(TimeSpan.FromSeconds(40));
            Assert.AreEqual(HttpStatusCode.InternalServerError, unavailable.StatusCode);
            Assert.AreEqual(0, Volatile.Read(ref faults._uploadsDuringPause));
        }
        finally
        {
            faults._release.TrySetResult();
            // 失败时也等待在途请求退出，避免宿主释放与专属文件目录清理抢跑。
            try { (await paused.WaitAsync(TimeSpan.FromSeconds(40))).Dispose(); }
            catch (Exception) { }
        }
        using (var restored = await management.PostAsJsonAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}/reactivate",
            new ReactivateTenantRequest(suspended!.Version)))
            Assert.AreEqual(HttpStatusCode.OK, restored.StatusCode, await restored.Content.ReadAsStringAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
            var pending = new SqlStatement("test.ai.unavailable_import",
                "SELECT COUNT(*) FROM fn_ai_knowledge_import AS operation INNER JOIN fn_ai_knowledge_document AS document ON document.Id=operation.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE document.Id=@DocumentId AND knowledge.TenantId=@TenantId AND operation.VersionId=@VersionId AND operation.StateKey='awaiting_upload' AND operation.FileId IS NULL AND operation.LeaseToken IS NULL",
                SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(
                pending, new Dictionary<string, object?> { ["DocumentId"] = document.Id, ["VersionId"] = versionId }));
        }
        // 原上传键在新 HTTP 作用域复用 Worker 已恢复对象，不创建第二个文件和版本。
        using var replay = await client.PostAsync(path + $"/versions?version=1&requestKey={requestKey}&fileName=source.txt", new StringContent(text, Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode, await replay.Content.ReadAsStringAsync());
        var operation = (await replay.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        Assert.AreEqual(versionId, operation.VersionId);
        Assert.AreEqual("completed", operation.State);
        Assert.AreEqual(0, Volatile.Read(ref faults._uploadsDuringPause));
        using var source = await client.GetAsync(path + $"/versions/{versionId}/source");
        Assert.AreEqual(text, await source.Content.ReadAsStringAsync());
        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalServices = finalScope.ServiceProvider;
        var context = finalServices.GetRequiredService<ICurrentTenantContextWriter>();
        context.SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
        Assert.AreEqual(fileId, (await finalServices.GetRequiredService<ITenantResourceFileStore>().ListReadyAsync("ai", versionId)).Single().FileId);
        context.SetHost();
        var usage = await finalServices.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(new SqlStatement("test.ai.quota_usage",
            "SELECT SUM(UsedValue) FROM fn_tenancy_quota_metric WHERE TenantId=@RequestedTenantId AND MetricCode='files.storage_bytes'", SqlDataScope.HostOnly), new Dictionary<string, object?> { ["RequestedTenantId"] = tenant.Id });
        Assert.AreEqual((long)Encoding.UTF8.GetByteCount(text), usage);
    }
    private sealed class PausedFiles(ITenantResourceFileStore inner, AiKnowledgeTenantImportRecovery faults) : ITenantResourceFileStore
    {
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId,
            string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default)
        {
            if (ownerModuleKey == "ai" && Volatile.Read(ref faults._trackingUploads) == 1)
                Interlocked.Increment(ref faults._uploadsDuringPause);
            return inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, contentLength, cancellationToken);
        }
        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
        public async Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId,
            CancellationToken cancellationToken = default)
        {
            if (ownerModuleKey != "ai" || Interlocked.Exchange(ref faults._pauseNextList, 0) == 0)
                return await inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
            faults._entered.TrySetResult();
            await faults._release.Task.WaitAsync(cancellationToken);
            try { return await inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken); }
            catch (TenantResourceFileUnavailableException)
            {
                faults._unavailable.TrySetResult();
                throw;
            }
        }
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
    private sealed class LaterClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddHours(1); }
    private sealed class LostConfirmation(ITenantFileStorageQuotaPort inner, AiKnowledgeTenantImportRecovery faults) : ITenantFileStorageQuotaPort
    {
        public Task<Result<bool>> TryReserveAsync(Guid tenantId, string operationId, long byteCount, CancellationToken cancellationToken = default) => inner.TryReserveAsync(tenantId, operationId, byteCount, cancellationToken);
        public async Task<Result<bool>> ConfirmAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default)
        {
            var result = await inner.ConfirmAsync(tenantId, operationId, cancellationToken);
            if (result.IsSuccess && result.Value && Interlocked.Exchange(ref faults._confirmationLost, 1) == 0) throw new IOException("Quota response lost after actual commit");
            return result;
        }
        public Task<Result<bool>> ReleaseAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) => inner.ReleaseAsync(tenantId, operationId, cancellationToken);
    }
}
