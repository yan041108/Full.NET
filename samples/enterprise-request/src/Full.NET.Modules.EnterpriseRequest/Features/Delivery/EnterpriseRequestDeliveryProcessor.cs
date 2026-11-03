using System.Security.Cryptography;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>处理已持久化意图；所有跨模块校验和文件 I/O 均位于应用短事务外。</summary>
internal sealed class EnterpriseRequestDeliveryProcessor(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IClock clock, ITenantResourceFileStore files, ITenantResourceFileCleanup cleanup,
    ITenantResourceFileDescriptorReader descriptors, EnterpriseRequestDeliveryAccess access,
    EnterpriseRequestDeliverySnapshotReader snapshots, EnterpriseRequestDocumentRenderer renderer, IOptions<DatabaseOptions> database)
{
    internal static readonly string[] ReportPermissions = [EnterpriseRequestPermissions.Read, EnterpriseRequestDetailPermissions.Read,
        EnterpriseRequestDeliveryPermissions.ReportsCreate, EnterpriseRequestDeliveryPermissions.ReportsRead];
    internal const string WorkbookType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    internal async Task<Result<bool>> ProcessAsync(Guid id, Stream? upload, CancellationToken token)
    {
        var work = await FindAsync(id, token).ConfigureAwait(false);
        if (work is null) return Failure("enterprise_request.delivery_not_found");
        var lease = Guid.CreateVersion7();
        if (await commands.ExecuteAsync(EnterpriseRequestDeliverySql.Acquire, P(("Id", id), ("LeaseId", lease),
            ("Now", clock.UtcNow), ("Until", clock.UtcNow.AddSeconds(45))), token).ConfigureAwait(false) != 1)
            return Result<bool>.Success(false);
        try
        {
            work = await FindAsync(id, token).ConfigureAwait(false) ?? throw new InvalidOperationException("Claimed delivery disappeared.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var ct = deadline.Token;
            if (CleanupPhase(work.Phase)) return Result<bool>.Success(await CleanAsync(work, lease, ct).ConfigureAwait(false));
            if (await queries.QuerySingleOrDefaultAsync<long>(EnterpriseRequestDeliverySql.ParentExists,
                P(("RequestId", work.RequestId)), ct).ConfigureAwait(false) == 0)
            {
                await MoveAsync(work, lease, "DeletedCleanup", "enterprise_request.delivery_source_deleted", ct).ConfigureAwait(false);
                return Result<bool>.Success(false);
            }
            if (work.ExpiresAtUtc <= clock.UtcNow)
            {
                await MoveAsync(work, lease, work.Kind == "Attachment" ? "Deleting" : "ExpiredCleanup",
                    "enterprise_request.delivery_expired", ct).ConfigureAwait(false);
                return Result<bool>.Success(false);
            }

            Guid? file;
            if (work.Kind == "Attachment" && work.Phase == "Uploading")
            {
                var authorized = await access.WriteAsync(work.RequestId, work.Binding(), work.RequestedVersion,
                    EnterpriseRequestDeliveryPermissions.AttachmentsManage, ct).ConfigureAwait(false);
                if (!authorized.IsSuccess) return await RejectAsync(work, lease, authorized.Error!.Code, ct).ConfigureAwait(false);
                file = await FindUploadedAsync(work, ct).ConfigureAwait(false);
                if (file is null && upload is not null)
                {
                    var uploaded = await files.UploadAsync(work.Owner, work.Id, work.ActorUserId, work.FileName,
                        work.ContentType, upload, work.SizeBytes!.Value, ct).ConfigureAwait(false);
                    if (!uploaded.IsSuccess) return await RejectAsync(work, lease, uploaded.Error!.Code, ct).ConfigureAwait(false);
                    if (uploaded.Value!.SizeBytes != work.SizeBytes || !SameHash(uploaded.Value.ContentHash, work.ContentHash))
                        return await RejectAsync(work, lease, "enterprise_request.delivery_content_mismatch", ct).ConfigureAwait(false);
                    file = uploaded.Value.FileId;
                }
                if (file is null) return Result<bool>.Success(false);
                authorized = await access.WriteAsync(work.RequestId, work.Binding(), work.RequestedVersion,
                    EnterpriseRequestDeliveryPermissions.AttachmentsManage, ct).ConfigureAwait(false);
                if (!authorized.IsSuccess) return await RejectAsync(work, lease, authorized.Error!.Code, ct).ConfigureAwait(false);
            }
            else if (work.Kind == "Report" && work.Phase == "Running")
            {
                var source = await snapshots.ReadAsync(work.RequestId, work.Binding(), ReportPermissions,
                    work.RequestedVersion, work.IncludesAmounts, ct).ConfigureAwait(false);
                if (!source.IsSuccess) return await RejectAsync(work, lease, source.Error!.Code, ct).ConfigureAwait(false);
                if (source.Value!.Digest() != work.SourceDigest)
                    return await RejectAsync(work, lease, "enterprise_request.delivery_version_conflict", ct).ConfigureAwait(false);
                file = await FindUploadedAsync(work, ct).ConfigureAwait(false);
                if (file is null)
                {
                    var rendered = renderer.Workbook(source.Value, work.LocaleCode, ct);
                    if (!rendered.IsSuccess) return await RejectAsync(work, lease, rendered.Error!.Code, ct).ConfigureAwait(false);
                    var bytes = rendered.Value!;
                    var hash = Convert.ToHexString(SHA256.HashData(bytes));
                    if (await commands.ExecuteAsync(EnterpriseRequestDeliverySql.StoreContent,
                        P(("Id", id), ("LeaseId", lease), ("ContentHash", hash), ("SizeBytes", (long)bytes.Length), ("Now", clock.UtcNow)), ct).ConfigureAwait(false) != 1)
                        return Failure("enterprise_request.delivery_version_conflict");
                    work = work with { ContentHash = hash, SizeBytes = bytes.Length };
                    using var stream = new MemoryStream(bytes, writable: false);
                    var uploaded = await files.UploadAsync(work.Owner, id, work.ActorUserId, work.FileName,
                        WorkbookType, stream, bytes.Length, ct).ConfigureAwait(false);
                    if (!uploaded.IsSuccess) return await RejectAsync(work, lease, uploaded.Error!.Code, ct).ConfigureAwait(false);
                    if (!SameHash(uploaded.Value!.ContentHash, hash) || uploaded.Value.SizeBytes != bytes.Length)
                        return await RejectAsync(work, lease, "enterprise_request.delivery_content_mismatch", ct).ConfigureAwait(false);
                    file = uploaded.Value.FileId;
                }
                source = await snapshots.ReadAsync(work.RequestId, work.Binding(), ReportPermissions,
                    work.RequestedVersion, work.IncludesAmounts, ct).ConfigureAwait(false);
                if (!source.IsSuccess) return await RejectAsync(work, lease, source.Error!.Code, ct).ConfigureAwait(false);
                if (source.Value!.Digest() != work.SourceDigest)
                    return await RejectAsync(work, lease, "enterprise_request.delivery_version_conflict", ct).ConfigureAwait(false);
            }
            else return Failure("enterprise_request.delivery_state_invalid");

            var result = await BindAsync(work, lease, file.Value, ct).ConfigureAwait(false);
            if (!result.IsSuccess) await MoveAsync(work, lease, "FailedCleanup", result.Error!.Code, ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception) when (work.Kind == "Report" && work.Attempt >= 3)
        {
            await MoveAsync(work, lease, "FailedCleanup", "enterprise_request.delivery_failed", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await commands.ExecuteAsync(EnterpriseRequestDeliverySql.Release, P(("Id", id), ("LeaseId", lease),
                ("DueAtUtc", clock.UtcNow.AddSeconds(Math.Min(60, 5 * Math.Max(1, work.Attempt))))), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<Result<bool>> BindAsync(EnterpriseRequestDeliveryRecord work, Guid lease, Guid file, CancellationToken token) =>
        await transaction.ExecuteResultAsync(async local =>
        {
            var p = P(("Id", work.Id), ("RequestId", work.RequestId), ("RequestedVersion", work.RequestedVersion),
                ("ActorUserId", work.ActorUserId), ("LeaseId", lease), ("FileId", file), ("ContentHash", work.ContentHash),
                ("ExpectedPhase", work.Phase), ("Now", clock.UtcNow));
            if (await queries.QuerySingleOrDefaultAsync<long>(database.Value.Provider == DatabaseProvider.MySql
                    ? EnterpriseRequestDeliverySql.LockParentMySql : EnterpriseRequestDeliverySql.LockParentSqlServer, p, local).ConfigureAwait(false) == 0)
                return Failure("enterprise_request.delivery_version_conflict");
            if (work.Kind == "Attachment" && await commands.ExecuteAsync(EnterpriseRequestDeliverySql.TouchParent, p, local).ConfigureAwait(false) != 1)
                return Failure("enterprise_request.delivery_version_conflict");
            return await commands.ExecuteAsync(EnterpriseRequestDeliverySql.BindFile, p, local).ConfigureAwait(false) == 1
                ? Result<bool>.Success(true) : Failure("enterprise_request.delivery_version_conflict");
        }, token).ConfigureAwait(false);

    private async Task<Guid?> FindUploadedAsync(EnterpriseRequestDeliveryRecord work, CancellationToken token)
    {
        if (work.ContentHash is null || work.SizeBytes is null) return null;
        var candidates = await files.ListReadyAsync(work.Owner, work.Id, token).ConfigureAwait(false);
        foreach (var item in candidates.Take(5))
        {
            var descriptor = await descriptors.GetReadyDescriptorAsync(work.Owner, work.Id, item.FileId, token).ConfigureAwait(false);
            if (descriptor is not null && descriptor.CreatedByUserId == work.ActorUserId && descriptor.SizeBytes == work.SizeBytes
                && descriptor.ContentType == work.ContentType && descriptor.OriginalFileName == work.FileName
                && SameHash(descriptor.ContentHash, work.ContentHash)) return item.FileId;
        }
        return null;
    }

    private async Task<Result<bool>> RejectAsync(EnterpriseRequestDeliveryRecord work, Guid lease, string code, CancellationToken token)
    {
        await MoveAsync(work, lease, "FailedCleanup", code, token).ConfigureAwait(false);
        return Failure(code);
    }
    private Task<int> MoveAsync(EnterpriseRequestDeliveryRecord work, Guid lease, string phase, string? code, CancellationToken token) =>
        commands.ExecuteAsync(EnterpriseRequestDeliverySql.SavePhase, P(("Id", work.Id), ("LeaseId", lease),
            ("ExpectedPhase", work.Phase), ("Phase", phase), ("ErrorCode", code), ("Now", clock.UtcNow)), token);

    private async Task<bool> CleanAsync(EnterpriseRequestDeliveryRecord work, Guid lease, CancellationToken token)
    {
        if (!await cleanup.ReleaseOwnedAsync(work.Owner, work.Id, token).ConfigureAwait(false)) return false;
        var terminal = work.Phase switch { "FailedCleanup" => "Failed", "CancelledCleanup" => "Cancelled",
            "ExpiredCleanup" => "Expired", _ => "Deleted" };
        return await commands.ExecuteAsync(EnterpriseRequestDeliverySql.FinishCleanup, P(("Id", work.Id), ("LeaseId", lease),
            ("ExpectedPhase", work.Phase), ("Phase", terminal), ("Now", clock.UtcNow)), token).ConfigureAwait(false) == 1;
    }
    internal Task<EnterpriseRequestDeliveryRecord?> FindAsync(Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<EnterpriseRequestDeliveryRecord>(EnterpriseRequestDeliverySql.Find, P(("Id", id)), token);
    private static bool SameHash(string? left, string? right) => left is not null && right is not null
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool CleanupPhase(string phase) => phase is "Deleting" or "FailedCleanup" or "CancelledCleanup" or "ExpiredCleanup" or "DeletedCleanup";
    private static Dictionary<string, object?> P(params (string Name, object? Value)[] pairs) => EnterpriseRequestDeliverySql.Parameters(pairs);
    private static Result<bool> Failure(string code) => Result<bool>.Failure(new Error(code, "The delivery could not be completed under the current authorization or source version.", ErrorType.Conflict));
}
