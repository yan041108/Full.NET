using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只在真实端口外注入响应故障，数据库提交和 Files 状态机仍使用生产实现。</summary>
internal sealed class AiKnowledgeImportFaults
{
    private readonly AiKnowledgeAbandonedImportLease _abandonedLease = new();
    internal bool ConfirmFails;
    internal bool ClaimFails;
    internal bool ReleaseFails;
    internal bool LosePublicationResult;
    internal bool PublicationWritten;
    internal bool InterruptBeforeClaim;
    internal bool InterruptAfterClaim;
    internal bool? OpenClaimAtInterruption;
    internal List<HostFileReferenceClaimRequest> ClaimRequests { get; } = [];
    internal List<HostFileReferenceClaimResult> ClaimResults { get; } = [];
    internal HostFileReferenceClaimResult? ConfirmedClaim;
    internal TaskCompletionSource<bool>? ConfirmEntered;
    internal TaskCompletionSource<bool>? ConfirmContinue;
    internal int Releases;

    internal void Configure(IServiceCollection services)
    {
        var command = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
        var transaction = services.Last(item => item.ServiceType == typeof(ICommandTransaction));
        services.RemoveAll<ICommandExecutor>();
        services.RemoveAll<ICommandTransaction>();
        services.AddScoped<ICommandExecutor>(provider => new Commands((ICommandExecutor)Resolve(provider, command), this));
        services.AddScoped<ICommandTransaction>(provider => new Transactions((ICommandTransaction)Resolve(provider, transaction), this));
        services.RemoveAll<IHostFileReferenceClaimService>();
        services.AddScoped<IHostFileReferenceClaimService>(provider => new Claims(ActivatorUtilities.CreateInstance<HostFileReferenceClaimService>(provider), this));
        _abandonedLease.Configure(services);
    }

    private static object Resolve(IServiceProvider provider, ServiceDescriptor descriptor) => descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

    internal static async Task VerifyAsync(FullNetApiFactory factory, AiKnowledgeImportFaults faults)
    {
        await factory.InitializeAsync();
        var owner = await factory.CreateHostIdentityAsync($"import-fault-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Create, AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Create,
             AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.VersionsRead,
             AiKnowledgeDocumentPermissions.ImportsRead, AiKnowledgeDocumentPermissions.ImportsRetry,
             AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Delete]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var createdBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("恢复故障", null));
        var catalog = (await createdBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var documents = $"/api/v1/ai/knowledge-bases/{catalog.Id}/documents";
        async Task<string> NewDocument()
        {
            using var response = await client.PostAsJsonAsync(documents, new CreateAiKnowledgeDocumentRequest("故障样本", null));
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
            return documents + "/" + (await response.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!.Id;
        }
        async Task<HttpResponseMessage> Upload(string path, Guid? key = null) => await client.PostAsync(path + $"/versions?version=1&requestKey={key ?? Guid.NewGuid()}&fileName=source.txt",
            new StringContent("真实文件", Encoding.UTF8, "text/plain"));
        async Task<AiKnowledgeImportResponse> Retry(string path, Guid operation)
        {
            using var response = await client.PostAsync(path + $"/imports/{operation}/retry", null);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        }
        async Task<long> Versions(string path) => (await client.GetFromJsonAsync<PagedResult<AiKnowledgeVersionResponse>>(path + "/versions"))!.Total;

        // 分别在 Files Claim 调用前和真实 Pending 提交后中断；新请求只续跑既有日志。
        foreach (var afterClaim in new[] { false, true })
        {
            var interruptedPath = await NewDocument();
            var interruptedKey = Guid.NewGuid();
            var releasesBeforeInterruption = faults.Releases;
            faults.ClaimRequests.Clear();
            faults.ClaimResults.Clear();
            faults.ConfirmedClaim = null;
            faults.OpenClaimAtInterruption = null;
            faults.InterruptBeforeClaim = !afterClaim;
            faults.InterruptAfterClaim = afterClaim;
            using var interrupted = await Upload(interruptedPath, interruptedKey);
            Assert.AreEqual(HttpStatusCode.InternalServerError, interrupted.StatusCode);
            Assert.AreEqual(afterClaim, faults.OpenClaimAtInterruption);
            Assert.AreEqual(releasesBeforeInterruption, faults.Releases);
            Assert.AreEqual(0L, await Versions(interruptedPath));
            Assert.AreEqual(1, (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(interruptedPath))!.Version);
            Assert.AreEqual(1, faults.ClaimRequests.Count);
            Assert.AreEqual(afterClaim ? 1 : 0, faults.ClaimResults.Count);
            var originalClaim = faults.ClaimRequests.Single();
            if (afterClaim) Assert.AreEqual(HostFileReferenceClaimStates.Pending, faults.ClaimResults.Single().State);
            using var unpublishedSource = await client.GetAsync(interruptedPath + $"/versions/{originalClaim.ConsumerReferenceId}/source");
            Assert.AreEqual(HttpStatusCode.NotFound, unpublishedSource.StatusCode);

            // 原始重试键和内容复用同一文件、版本及 Claim，不把未知结果改成补偿。
            using var resumed = await Upload(interruptedPath, interruptedKey);
            Assert.AreEqual(HttpStatusCode.Accepted, resumed.StatusCode, await resumed.Content.ReadAsStringAsync());
            var resumedOperation = (await resumed.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
            Assert.AreEqual("completed", resumedOperation.State);
            Assert.AreEqual(originalClaim.ConsumerReferenceId, resumedOperation.VersionId);
            Assert.AreEqual(2, faults.ClaimRequests.Count);
            Assert.AreEqual(originalClaim, faults.ClaimRequests[1]);
            Assert.AreEqual(afterClaim ? 2 : 1, faults.ClaimResults.Count);
            Assert.IsTrue(faults.ClaimResults.All(item => item.ClaimId == faults.ClaimResults[0].ClaimId));
            Assert.IsNotNull(faults.ConfirmedClaim);
            Assert.AreEqual(faults.ClaimResults[0].ClaimId, faults.ConfirmedClaim.ClaimId);
            Assert.AreEqual(originalClaim.FileId, faults.ConfirmedClaim.FileReference.FileId);
            Assert.AreEqual(HostFileReferenceClaimStates.Active, faults.ConfirmedClaim.State);
            Assert.AreEqual(releasesBeforeInterruption, faults.Releases);
            Assert.AreEqual(1L, await Versions(interruptedPath));
            Assert.AreEqual(2, (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(interruptedPath))!.Version);
            using var readable = await client.GetAsync(interruptedPath + $"/versions/{resumedOperation.VersionId}/source");
            Assert.AreEqual(HttpStatusCode.OK, readable.StatusCode, await readable.Content.ReadAsStringAsync());
            Assert.AreEqual("真实文件", await readable.Content.ReadAsStringAsync());
            using var completedReplay = await Upload(interruptedPath, interruptedKey);
            Assert.AreEqual(HttpStatusCode.Accepted, completedReplay.StatusCode);
            Assert.AreEqual(resumedOperation.Id, (await completedReplay.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.Id);
            Assert.AreEqual(2, faults.ClaimRequests.Count);
            Assert.AreEqual(1L, await Versions(interruptedPath));
        }

        // Confirm 已知失败保留权威提交，新 HTTP 作用域重试不得创建第二个版本。
        var confirmPath = await NewDocument();
        faults.ConfirmFails = true;
        using var pendingResponse = await Upload(confirmPath);
        Assert.AreEqual(HttpStatusCode.Accepted, pendingResponse.StatusCode);
        var pending = (await pendingResponse.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        Assert.AreEqual("pending_confirmation", pending.State);
        Assert.AreEqual(1L, await Versions(confirmPath));
        faults.ConfirmFails = false;
        Assert.AreEqual("completed", (await Retry(confirmPath, pending.Id)).State);
        Assert.AreEqual(1L, await Versions(confirmPath));

        // AI 已提交但响应丢失：不补偿已持有的文件，恢复从日志继续确认。
        var unknownPath = await NewDocument();
        var releasesBefore = faults.Releases;
        faults.LosePublicationResult = true;
        var unknownKey = Guid.NewGuid();
        using var lostResponse = await Upload(unknownPath, unknownKey);
        Assert.AreEqual(HttpStatusCode.InternalServerError, lostResponse.StatusCode);
        Assert.AreEqual(releasesBefore, faults.Releases);
        var unknownVersion = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeVersionResponse>>(unknownPath + "/versions"))!.Items.Single();
        // 由请求键定位既有操作，重复原始上传不会再次推进文档版本。
        Assert.AreEqual(1L, await Versions(unknownPath));
        Assert.AreEqual("pending_processing", unknownVersion.ProcessingStatus);
        using var recoveredResponse = await Upload(unknownPath, unknownKey);
        Assert.AreEqual(HttpStatusCode.Accepted, recoveredResponse.StatusCode);
        Assert.AreEqual("completed", (await recoveredResponse.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.State);
        Assert.AreEqual(1L, await Versions(unknownPath));
        Assert.AreEqual(2, (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(unknownPath))!.Version);

        // Claim 明确拒绝且释放失败，补偿意图仍可在新请求恢复。
        var cleanupPath = await NewDocument();
        faults.ClaimFails = true;
        faults.ReleaseFails = true;
        var retryKey = Guid.NewGuid();
        using var denied = await client.PostAsync(cleanupPath + $"/versions?version=1&requestKey={retryKey}&fileName=source.txt", new StringContent("真实文件", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Conflict, denied.StatusCode);
        faults.ClaimFails = false;
        using var cleanupReplay = await client.PostAsync(cleanupPath + $"/versions?version=1&requestKey={retryKey}&fileName=source.txt", new StringContent("真实文件", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Accepted, cleanupReplay.StatusCode);
        var cleanup = (await cleanupReplay.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        Assert.AreEqual("cleanup_pending", cleanup.State);
        Assert.AreEqual(0L, await Versions(cleanupPath));
        faults.ReleaseFails = false;
        Assert.AreEqual("failed", (await Retry(cleanupPath, cleanup.Id)).State);

        // 删除发生在 Confirm 等待期间，成功路径不能复活清理状态。
        var racePath = await NewDocument();
        faults.ConfirmEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        faults.ConfirmContinue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploading = Upload(racePath);
        await faults.ConfirmEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            using var deletion = new HttpRequestMessage(HttpMethod.Delete, racePath) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(2)) };
            using var deleted = await client.SendAsync(deletion);
            Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());
        }
        finally { faults.ConfirmContinue.TrySetResult(true); }
        using var raced = await uploading;
        Assert.AreEqual(HttpStatusCode.Accepted, raced.StatusCode, await raced.Content.ReadAsStringAsync());
        var racedOperation = (await raced.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        Assert.AreEqual("cleanup_pending", racedOperation.State);
        faults.ConfirmEntered = null;
        Assert.AreEqual("failed", (await Retry(racePath, racedOperation.Id)).State);
        await faults._abandonedLease.VerifyAsync(factory, faults);
    }

    private sealed class Commands(ICommandExecutor inner, AiKnowledgeImportFaults faults) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var result = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (result == 1 && statement.Name.StartsWith("ai.knowledge_import.set_state", StringComparison.Ordinal)
                && ((IReadOnlyDictionary<string, object?>)parameters!)["StateKey"] as string == "pending_confirmation")
                faults.PublicationWritten = true;
            return result;
        }
    }
    private sealed class Transactions(ICommandTransaction inner, AiKnowledgeImportFaults faults) : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => inner.ExecuteAsync(action, cancellationToken);
        public async Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken cancellationToken)
        {
            faults.PublicationWritten = false;
            var result = await inner.ExecuteResultAsync(action, cancellationToken);
            if (faults.LosePublicationResult && faults.PublicationWritten && result.IsSuccess)
            {
                faults.LosePublicationResult = false;
                throw new IOException("AI commit response lost after actual commit");
            }
            return result;
        }
    }
    private sealed class Claims(IHostFileReferenceClaimService inner, AiKnowledgeImportFaults faults) : IHostFileReferenceClaimService
    {
        public async Task<Result<HostFileReferenceClaimResult>> ClaimAsync(HostFileReferenceClaimRequest request, CancellationToken cancellationToken = default)
        {
            faults.ClaimRequests.Add(request);
            if (faults.InterruptBeforeClaim)
            {
                faults.InterruptBeforeClaim = false;
                faults.OpenClaimAtInterruption = await inner.HasOpenClaimsAsync(request.FileId, cancellationToken);
                throw new IOException("Injected interruption before Files claim");
            }
            if (faults.ClaimFails)
                return Result<HostFileReferenceClaimResult>.Failure(new Error("files.test.claim_denied", "Injected denial", ErrorType.Conflict));
            var result = await inner.ClaimAsync(request, cancellationToken);
            if (result.IsSuccess) faults.ClaimResults.Add(result.Value!);
            if (faults.InterruptAfterClaim && result.IsSuccess)
            {
                faults.InterruptAfterClaim = false;
                faults.OpenClaimAtInterruption = await inner.HasOpenClaimsAsync(request.FileId, cancellationToken);
                throw new IOException("Injected response loss after Files claim commit");
            }
            return result;
        }
        public async Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            if (faults.ConfirmFails) return Result<HostFileReferenceClaimResult>.Failure(new Error("files.test.confirm_unavailable", "Injected failure", ErrorType.Conflict));
            if (faults.ConfirmEntered is { } entered) { entered.TrySetResult(true); await faults.ConfirmContinue!.Task.WaitAsync(cancellationToken); }
            var result = await inner.ConfirmAsync(idempotencyKey, cancellationToken);
            if (result.IsSuccess) faults.ConfirmedClaim = result.Value!;
            return result;
        }
        public Task<Result<bool>> ReleaseAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref faults.Releases);
            return faults.ReleaseFails ? Task.FromResult(Result<bool>.Failure(new Error("files.test.release_unavailable", "Injected failure", ErrorType.Conflict))) : inner.ReleaseAsync(idempotencyKey, cancellationToken);
        }
        public Task<bool> HasOpenClaimsAsync(Guid fileId, CancellationToken cancellationToken = default) => inner.HasOpenClaimsAsync(fileId, cancellationToken);
    }
}
