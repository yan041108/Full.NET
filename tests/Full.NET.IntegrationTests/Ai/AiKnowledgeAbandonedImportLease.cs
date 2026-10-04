using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>保留真实提交的租约并推进夹具时钟，验证新请求接管与旧令牌隔离；不模拟真实进程终止。</summary>
internal sealed class AiKnowledgeAbandonedImportLease
{
    private readonly OffsetClock _clock = new();
    private int _skipLeaseRelease;
    private int _interruptSavedFile;
    private int _droppedReleases;
    private Guid? _faultDocument;
    private Guid? _gateOperation;
    private TaskCompletionSource<bool>? _acquired;
    private TaskCompletionSource<bool>? _continue;

    internal void Configure(IServiceCollection services)
    {
        var descriptor = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
        services.RemoveAll<ICommandExecutor>();
        services.AddScoped<ICommandExecutor>(provider => new Commands((ICommandExecutor)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), this));
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(_clock);
    }

    internal async Task VerifyAsync(FullNetApiFactory factory, AiKnowledgeImportFaults claims)
    {
        var owner = await factory.CreateHostIdentityAsync($"abandoned-import-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Create, AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Create,
             AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.ImportsRetry,
             AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);

        // Host 分别保留 Claim 前后遗留租约；Tenant 在绑定已上传文件后中断，覆盖两套作用域 SQL。
        foreach (var stage in new[] { "before_claim", "after_claim", "tenant_file_saved" })
        {
            var isTenant = stage == "tenant_file_saved";
            // 切换会话上下文会使旧 Host 令牌失效，因此先完成 Host 场景，再签发 Tenant 令牌。
            if (isTenant) tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id));
            var client = isTenant ? tenantClient : host;
            using var createdBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("遗留租约", null));
            Assert.AreEqual(HttpStatusCode.Created, createdBase.StatusCode, await createdBase.Content.ReadAsStringAsync());
            var catalog = (await createdBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            var documents = $"/api/v1/ai/knowledge-bases/{catalog.Id}/documents";
            using var createdDocument = await client.PostAsJsonAsync(documents, new CreateAiKnowledgeDocumentRequest("租约恢复", null));
            Assert.AreEqual(HttpStatusCode.Created, createdDocument.StatusCode, await createdDocument.Content.ReadAsStringAsync());
            var document = (await createdDocument.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
            var path = documents + $"/{document.Id}";
            var key = Guid.NewGuid();
            var parameters = new Dictionary<string, object?> { ["KnowledgeBaseId"] = catalog.Id, ["DocumentId"] = document.Id,
                ["OwnerUserId"] = owner.UserId, ["RequestKey"] = key };
            const string text = "到期后恢复同一文件";
            Task<HttpResponseMessage> Upload() => client.PostAsync(path + $"/versions?version=1&requestKey={key}&fileName=source.txt",
                new StringContent(text, Encoding.UTF8, "text/plain"));
            async Task<AiKnowledgeImportRecord> ReadAsync()
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (isTenant) writer.SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
                else writer.SetHost();
                var row = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiKnowledgeImportRecord>(
                    isTenant ? AiKnowledgeImportSql.FindKeyTenant : AiKnowledgeImportSql.FindKeyHost, parameters);
                Assert.IsNotNull(row);
                return row;
            }
            async Task ExpectBusyAsync(HttpResponseMessage response)
            {
                using (response)
                {
                    Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
                    Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);
                    using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.AreEqual(409, problem.RootElement.GetProperty("status").GetInt32());
                    Assert.AreEqual("ai.knowledge.import_busy", problem.RootElement.GetProperty("code").GetString());
                }
            }

            var releases = claims.Releases;
            var dropped = _droppedReleases;
            claims.ClaimRequests.Clear();
            claims.ClaimResults.Clear();
            claims.InterruptBeforeClaim = stage == "before_claim";
            claims.InterruptAfterClaim = stage == "after_claim";
            _faultDocument = document.Id;
            _interruptSavedFile = isTenant ? 1 : 0;
            _skipLeaseRelease = 1;
            try
            {
                using var interrupted = await Upload();
                Assert.AreEqual(HttpStatusCode.InternalServerError, interrupted.StatusCode);
                Assert.AreEqual(dropped + 1, _droppedReleases);
                var original = await ReadAsync();
                Assert.AreEqual("file_uploaded", original.StateKey);
                Assert.IsNotNull(original.FileId);
                Assert.IsNotNull(original.LeaseToken);
                Assert.IsNotNull(original.LeaseExpiresAtUtc);
                Assert.IsTrue(original.LeaseExpiresAtUtc > _clock.UtcNow.AddMinutes(4));
                Assert.IsTrue(original.LeaseExpiresAtUtc <= _clock.UtcNow.AddMinutes(5));
                var retryPath = path + $"/imports/{original.Id}/retry";
                await ExpectBusyAsync(await client.PostAsync(retryPath, null));
                await ExpectBusyAsync(await Upload());
                var held = await ReadAsync();
                Assert.AreEqual(original.LeaseToken, held.LeaseToken);
                Assert.AreEqual(original.LeaseExpiresAtUtc, held.LeaseExpiresAtUtc);
                Assert.AreEqual(original.FileId, held.FileId);
                Assert.AreEqual(0L, (await client.GetFromJsonAsync<PagedResult<AiKnowledgeVersionResponse>>(path + "/versions"))!.Total);
                Assert.AreEqual(1, (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(path))!.Version);
                Assert.AreEqual(releases, claims.Releases);

                _clock.Advance(TimeSpan.FromMinutes(6));
                _gateOperation = original.Id;
                _acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var winner = client.PostAsync(retryPath, null);
                try
                {
                    await _acquired.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    await ExpectBusyAsync(await client.PostAsync(retryPath, null));
                    var takeover = await ReadAsync();
                    Assert.IsNotNull(takeover.LeaseToken);
                    Assert.AreNotEqual(original.LeaseToken, takeover.LeaseToken);
                    Assert.IsTrue(takeover.LeaseExpiresAtUtc > _clock.UtcNow);

                    // 旧执行者即使恢复到清理路径，也不能取消新租约或改写其状态。
                    await using var staleScope = factory.Services.CreateAsyncScope();
                    var writer = staleScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                    if (isTenant) writer.SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
                    else writer.SetHost();
                    var stale = new Dictionary<string, object?>(parameters) { ["OperationId"] = original.Id,
                        ["LeaseToken"] = original.LeaseToken, ["Now"] = _clock.UtcNow,
                        ["StateKey"] = "cleanup_pending", ["FailureCode"] = "ai.knowledge.document_not_found" };
                    var command = staleScope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                    Assert.AreEqual(0, await command.ExecuteAsync(isTenant ? AiKnowledgeImportSql.SetStateTenant : AiKnowledgeImportSql.SetStateHost, stale));
                    Assert.AreEqual(0, await command.ExecuteAsync(isTenant ? AiKnowledgeImportSql.ReleaseLeaseTenant : AiKnowledgeImportSql.ReleaseLeaseHost, stale));
                    var fenced = await ReadAsync();
                    Assert.AreEqual(takeover.LeaseToken, fenced.LeaseToken);
                    Assert.AreEqual(takeover.LeaseExpiresAtUtc, fenced.LeaseExpiresAtUtc);
                    Assert.AreEqual("file_uploaded", fenced.StateKey);
                }
                catch
                {
                    _continue.TrySetResult(true);
                    using var failedAttempt = await winner.WaitAsync(TimeSpan.FromSeconds(30));
                    throw;
                }
                finally { _continue.TrySetResult(true); }
                using var resumed = await winner.WaitAsync(TimeSpan.FromSeconds(30));
                using (resumed)
                {
                    Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode, await resumed.Content.ReadAsStringAsync());
                    var response = (await resumed.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
                    Assert.AreEqual(original.Id, response.Id);
                    Assert.AreEqual(original.VersionId, response.VersionId);
                    Assert.AreEqual("completed", response.State);
                }
                var completed = await ReadAsync();
                Assert.AreEqual(original.FileId, completed.FileId);
                Assert.AreEqual(original.RequestKey, completed.RequestKey);
                Assert.IsNull(completed.LeaseToken);
                Assert.IsNull(completed.LeaseExpiresAtUtc);
                var versions = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeVersionResponse>>(path + "/versions"))!;
                Assert.AreEqual(1L, versions.Total);
                Assert.AreEqual(original.VersionId, versions.Items.Single().Id);
                Assert.AreEqual(2, (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(path))!.Version);
                using var source = await client.GetAsync(path + $"/versions/{original.VersionId}/source");
                Assert.AreEqual(HttpStatusCode.OK, source.StatusCode, await source.Content.ReadAsStringAsync());
                Assert.AreEqual(text, await source.Content.ReadAsStringAsync());
                var claimCount = claims.ClaimRequests.Count;
                using var replay = await Upload();
                Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode);
                Assert.AreEqual(original.Id, (await replay.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.Id);
                Assert.AreEqual(claimCount, claims.ClaimRequests.Count);
                Assert.AreEqual(1L, (await client.GetFromJsonAsync<PagedResult<AiKnowledgeVersionResponse>>(path + "/versions"))!.Total);
                Assert.AreEqual(releases, claims.Releases);
                if (isTenant)
                {
                    await using var scope = factory.Services.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
                    var ready = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().ListReadyAsync("ai", original.VersionId);
                    Assert.HasCount(1, ready);
                    Assert.AreEqual(original.FileId, ready.Single().FileId);
                }
                else
                {
                    Assert.AreEqual(2, claims.ClaimRequests.Count);
                    Assert.AreEqual(claims.ClaimRequests[0], claims.ClaimRequests[1]);
                    Assert.IsTrue(claims.ClaimResults.All(item => item.ClaimId == claims.ClaimResults[0].ClaimId));
                    Assert.AreEqual(HostFileReferenceClaimStates.Active, claims.ConfirmedClaim!.State);
                    Assert.AreEqual(original.FileId, claims.ConfirmedClaim.FileReference.FileId);
                }
            }
            finally
            {
                _continue?.TrySetResult(true);
                _gateOperation = null;
                _faultDocument = null;
                _skipLeaseRelease = 0;
                _interruptSavedFile = 0;
                _clock.Reset();
            }
        }
    }

    private sealed class OffsetClock : IClock
    {
        private long _offsetTicks;
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref _offsetTicks, duration.Ticks);
        internal void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
    }

    private sealed class Commands(ICommandExecutor inner, AiKnowledgeAbandonedImportLease faults) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            // 不执行这一次清理，保留此前真实领取的租约；故障不作用于文件释放或其他模块 SQL。
            var targetDocument = parameters is IReadOnlyDictionary<string, object?> values
                && faults._faultDocument is { } document && values.TryGetValue("DocumentId", out var documentValue)
                && documentValue is Guid documentId && documentId == document;
            if (targetDocument && statement.Name.StartsWith("ai.knowledge_import.release_lease_", StringComparison.Ordinal)
                && Interlocked.Exchange(ref faults._skipLeaseRelease, 0) == 1)
            {
                Interlocked.Increment(ref faults._droppedReleases);
                throw new IOException("Injected failure while releasing persisted import lease");
            }
            var result = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (targetDocument && result == 1 && statement.Name == AiKnowledgeImportSql.SaveFileTenant.Name
                && Interlocked.Exchange(ref faults._interruptSavedFile, 0) == 1)
                throw new IOException("Injected response loss after tenant import file binding");
            if (result == 1 && statement.Name.StartsWith("ai.knowledge_import.acquire_", StringComparison.Ordinal)
                && faults._gateOperation is { } operation
                && ((IReadOnlyDictionary<string, object?>)parameters!)["OperationId"] is Guid id && id == operation)
            {
                faults._acquired!.TrySetResult(true);
                await faults._continue!.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
