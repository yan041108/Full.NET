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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>配额提交后响应丢失，真实 Worker 两轮恢复不能删除未绑定来源或重复计量。</summary>
internal sealed class AiKnowledgeTenantImportRecovery
{
    internal void Configure(IServiceCollection services)
    {
        var descriptor = services.Last(item => item.ServiceType == typeof(ITenantFileStorageQuotaPort));
        services.RemoveAll<ITenantFileStorageQuotaPort>();
        services.AddScoped<ITenantFileStorageQuotaPort>(provider => new LostConfirmation(
            (ITenantFileStorageQuotaPort)(descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), this));
    }
    private int _confirmationLost;
    internal static async Task VerifyAsync(FullNetApiFactory factory)
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
        // 原上传键在新 HTTP 作用域复用 Worker 已恢复对象，不创建第二个文件和版本。
        using var replay = await client.PostAsync(path + $"/versions?version=1&requestKey={requestKey}&fileName=source.txt", new StringContent(text, Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode, await replay.Content.ReadAsStringAsync());
        var operation = (await replay.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        Assert.AreEqual(versionId, operation.VersionId);
        Assert.AreEqual("completed", operation.State);
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
