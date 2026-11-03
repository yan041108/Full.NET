using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.PreviewTasks;

/// <summary>引用意图先提交，跨模块 Claim 在事务外调用；迟到确认必须重查任务和租约。</summary>
internal sealed class DocumentPreviewLifecycle(
    IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction,
    IHostFileReferenceClaimService claims, DocumentMutationGuard guard,
    DocumentReferenceReleasePublisher releases, IClock clock,
    IOptions<DatabaseOptions> database, IOptionsMonitor<DocumentOfficePreviewConversionOptions> options)
{
    internal int LeaseSeconds => Math.Clamp(options.CurrentValue.TimeoutSeconds, 5, 3600) + 60;
    internal static string UploadKey(Guid outputReferenceId) => $"document-preview:{outputReferenceId:N}";

    internal Task<DocumentPreviewTaskRecord?> FindAsync(Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById, DocumentSqlParameters.Create(("Id", id)), token);
    internal Task<IReadOnlyList<DocumentPreviewReferenceRecord>> ReferencesAsync(Guid taskId, CancellationToken token) =>
        queries.QueryAsync<DocumentPreviewReferenceRecord>(DocumentPreviewReferenceSql.ByTask, DocumentSqlParameters.Create(("TaskId", taskId)), token);

    /// <summary>续租与永久删除共享根锁，清理捕获截止后不能再延长在途资源的租期。</summary>
    internal Task<bool> RenewAsync(DocumentPreviewTaskRecord task, DateTimeOffset now, int timeoutSeconds, CancellationToken token) =>
        transaction.ExecuteAsync(async ct =>
        {
            var item = await guard.LockAsync(task.DocumentItemId, ct).ConfigureAwait(false);
            if (item is null || item.DeletedAtUtc is not null || item.IsPurging) return false;
            return await commands.ExecuteAsync(DocumentPreviewTaskSql.RenewLease, DocumentSqlParameters.Create(("Id", task.Id),
                ("Version", task.Version), ("Now", now), ("LeaseExpiresAtUtc", now.AddSeconds(timeoutSeconds + 60))), ct).ConfigureAwait(false) == 1;
        }, token);

    internal async Task PrepareAsync(DocumentPreviewTaskRecord task, CancellationToken token)
    {
        var source = (await ReferencesAsync(task.Id, token).ConfigureAwait(false)).SingleOrDefault(r => r.Purpose == "source");
        if (source is null || source.StateKey is "released" or "release_pending") return;
        var claim = await claims.ClaimAsync(new HostFileReferenceClaimRequest(
            HostFileReferenceClaimIdempotencyKeys.DocumentPreview(source.Id), HostFileReferenceClaimConsumerModules.Document, source.Id, source.FileId), token).ConfigureAwait(false);
        var accepted = await transaction.ExecuteAsync(async ct =>
        {
            var item = await guard.LockAsync(task.DocumentItemId, ct).ConfigureAwait(false);
            var current = await FindAsync(task.Id, ct).ConfigureAwait(false);
            if (item is null || item.DeletedAtUtc is not null || item.IsPurging || current?.StatusKey != "preparing") return false;
            if (!claim.IsSuccess || claim.Value!.State == HostFileReferenceClaimStates.Released)
            {
                await commands.ExecuteAsync(DocumentPreviewTaskSql.PreparationFailed, DocumentSqlParameters.Create(("Id", task.Id),
                    ("ErrorCode", claim.Error?.Code ?? "document.preview.reference_released"), ("Now", clock.UtcNow)), ct).ConfigureAwait(false);
                await ReleaseTaskAsync(current, false, ct).ConfigureAwait(false);
                return false;
            }
            await commands.ExecuteAsync(DocumentPreviewReferenceSql.Claim, DocumentSqlParameters.Create(("Id", source.Id)), ct).ConfigureAwait(false);
            return await commands.ExecuteAsync(DocumentPreviewTaskSql.MakeRunnable, DocumentSqlParameters.Create(("Id", task.Id)), ct).ConfigureAwait(false) == 1;
        }, token).ConfigureAwait(false);
        // Confirm 失败保留 Pending，由 Files 精确 Probe 对账；取消后 Confirm 无法复活已释放的 Claim。
        if (accepted) await claims.ConfirmAsync(HostFileReferenceClaimIdempotencyKeys.DocumentPreview(source.Id), token).ConfigureAwait(false);
    }

    internal async Task<bool> CompleteAsync(DocumentPreviewTaskRecord task, Guid fileId, CancellationToken token)
    {
        var output = (await ReferencesAsync(task.Id, token).ConfigureAwait(false)).SingleOrDefault(r => r.Purpose == "output");
        if (output is null || output.FileId != fileId || output.StateKey is "released" or "release_pending") return false;
        var key = HostFileReferenceClaimIdempotencyKeys.DocumentPreview(output.Id);
        var claim = await claims.ClaimAsync(new HostFileReferenceClaimRequest(key, HostFileReferenceClaimConsumerModules.Document, output.Id, fileId), token).ConfigureAwait(false);
        if (!claim.IsSuccess || claim.Value!.State == HostFileReferenceClaimStates.Released) return false;
        // Claim 仍为可对账 Pending 时先检查意图；释放先于 Claim 消费时不能提前确认成 Active。
        if (!await transaction.ExecuteAsync(ct => AdmitCompletionAsync(task, output, ct), token).ConfigureAwait(false)) return false;
        var confirmed = await claims.ConfirmAsync(key, token).ConfigureAwait(false);
        if (!confirmed.IsSuccess || confirmed.Value!.State != HostFileReferenceClaimStates.Active) return false;
        return await transaction.ExecuteAsync(async ct =>
        {
            if (!await AdmitCompletionAsync(task, output, ct).ConfigureAwait(false)) return false;
            var changed = await commands.ExecuteAsync(DocumentPreviewTaskSql.MarkSucceeded, DocumentSqlParameters.Create(
                ("Id", task.Id), ("Version", task.Version), ("OutputFileId", fileId),
                ("ExpiredBefore", clock.UtcNow.AddSeconds(-LeaseSeconds)), ("Now", clock.UtcNow), ("CompletedAtUtc", clock.UtcNow)), ct).ConfigureAwait(false);
            if (changed != 1) return false;
            await commands.ExecuteAsync(DocumentPreviewReferenceSql.Claim, DocumentSqlParameters.Create(("Id", output.Id)), ct).ConfigureAwait(false);
            await FinishAttemptAsync(task, null, ct).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
    }

    /// <summary>确认前后复用根锁与当前意图校验；确认前中断留下 Pending，确认后中断仍有可靠释放身份。</summary>
    private async Task<bool> AdmitCompletionAsync(DocumentPreviewTaskRecord task, DocumentPreviewReferenceRecord output, CancellationToken token)
    {
        var item = await guard.LockAsync(task.DocumentItemId, token).ConfigureAwait(false);
        var currentOutput = (await ReferencesAsync(task.Id, token).ConfigureAwait(false)).SingleOrDefault(r => r.Id == output.Id);
        if (currentOutput?.StateKey is "released" or "release_pending")
        {
            await releases.PublishAsync(output.Id, output.FileId, token, true,
                currentOutput.ReleaseAfterUtc > clock.UtcNow ? currentOutput.ReleaseAfterUtc : null).ConfigureAwait(false);
            return false;
        }
        if (item is null || item.DeletedAtUtc is not null || item.IsPurging || currentOutput is null) return false;
        var current = await FindAsync(task.Id, token).ConfigureAwait(false);
        return current?.StatusKey == "processing" && current.Version == task.Version && current.LeaseExpiresAtUtc > clock.UtcNow;
    }

    internal Task FailAsync(DocumentPreviewTaskRecord task, string errorCode, bool retryable, CancellationToken token) =>
        transaction.ExecuteAsync(async ct =>
        {
            await guard.LockAsync(task.DocumentItemId, ct).ConfigureAwait(false);
            var retry = retryable && task.AttemptCount < options.CurrentValue.MaxAttempts;
            var changed = await commands.ExecuteAsync(DocumentPreviewTaskSql.MarkFailed, DocumentSqlParameters.Create(("Id", task.Id), ("Version", task.Version),
                ("ExpiredBefore", clock.UtcNow.AddSeconds(-LeaseSeconds)), ("Now", clock.UtcNow), ("StatusKey", retry ? "pending" : "failed"), ("ErrorCode", errorCode),
                ("CompletedAtUtc", retry ? (DateTimeOffset?)null : clock.UtcNow),
                ("NextAttemptAtUtc", retry ? clock.UtcNow.AddSeconds(options.CurrentValue.RetryBackoffSeconds * task.AttemptCount) : (DateTimeOffset?)null)), ct).ConfigureAwait(false);
            if (changed == 1)
            {
                await FinishAttemptAsync(task, errorCode, ct).ConfigureAwait(false);
                if (!retry) await ReleaseTaskAsync(task, false, ct).ConfigureAwait(false);
            }
            return true;
        }, token);

    internal Task FinishAttemptAsync(DocumentPreviewTaskRecord task, string? errorCode, CancellationToken token) =>
        commands.ExecuteAsync(DocumentPreviewTaskSql.FinishAttempt, DocumentSqlParameters.Create(("Id", task.Id), ("Version", task.Version), ("ErrorCode", errorCode), ("Now", clock.UtcNow)), token);

    /// <summary>在调用方本地事务持久化释放；源保留至任务取消或永久删除，输出失败立即进入释放流程。</summary>
    internal async Task ReleaseTaskAsync(DocumentPreviewTaskRecord task, bool includeSource, CancellationToken token)
    {
        var latest = await FindAsync(task.Id, token).ConfigureAwait(false) ?? task;
        var notBefore = latest.LeaseExpiresAtUtc ?? clock.UtcNow;
        foreach (var reference in await ReferencesAsync(task.Id, token).ConfigureAwait(false))
            if (includeSource || reference.Purpose == "output") await ReleaseReferenceAsync(reference, notBefore, token).ConfigureAwait(false);
    }

    internal async Task ReleaseReferenceAsync(DocumentPreviewReferenceRecord reference, DateTimeOffset notBefore, CancellationToken token)
    {
        var pending = notBefore > clock.UtcNow;
        var changed = await commands.ExecuteAsync(DocumentPreviewReferenceSql.Release, DocumentSqlParameters.Create(("Id", reference.Id),
            ("StateKey", pending ? "release_pending" : "released"), ("ReleaseAfterUtc", notBefore)), token).ConfigureAwait(false);
        if (changed == 1) await releases.PublishAsync(reference.Id, reference.FileId, token, true, pending ? notBefore : null).ConfigureAwait(false);
    }

    internal async Task MaintainAsync(CancellationToken token)
    {
        var due = await queries.QueryAsync<DocumentPreviewReferenceRecord>(database.Value.Provider == DatabaseProvider.MySql
            ? DocumentPreviewReferenceSql.DueMySql : DocumentPreviewReferenceSql.DueSqlServer,
            DocumentSqlParameters.Create(("Now", clock.UtcNow)), token).ConfigureAwait(false);
        foreach (var reference in due)
            await transaction.ExecuteAsync(async ct =>
            {
                var changed = await commands.ExecuteAsync(DocumentPreviewReferenceSql.FinalRelease,
                    DocumentSqlParameters.Create(("Id", reference.Id), ("Now", clock.UtcNow)), ct).ConfigureAwait(false);
                if (changed == 1) await releases.PublishAsync(reference.Id, reference.FileId, ct, true).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        var tasks = await queries.QueryAsync<DocumentPreviewTaskRecord>(database.Value.Provider == DatabaseProvider.MySql
            ? DocumentPreviewTaskSql.MaintenanceMySql : DocumentPreviewTaskSql.MaintenanceSqlServer, null, token).ConfigureAwait(false);
        foreach (var task in tasks)
        {
            if (task.StatusKey == "preparing") await PrepareAsync(task, token).ConfigureAwait(false);
            else await transaction.ExecuteAsync(async ct =>
            {
                await guard.LockAsync(task.DocumentItemId, ct).ConfigureAwait(false);
                var current = await FindAsync(task.Id, ct).ConfigureAwait(false);
                if (current?.StatusKey is "failed" or "cancelled") await ReleaseTaskAsync(current, current.StatusKey == "cancelled", ct).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }
        await transaction.ExecuteAsync(async ct =>
        {
            await commands.ExecuteAsync(database.Value.Provider == DatabaseProvider.MySql ? DocumentDeletionFactSql.CleanMySql : DocumentDeletionFactSql.CleanSqlServer,
                DocumentSqlParameters.Create(("Now", clock.UtcNow)), ct).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
    }
}
