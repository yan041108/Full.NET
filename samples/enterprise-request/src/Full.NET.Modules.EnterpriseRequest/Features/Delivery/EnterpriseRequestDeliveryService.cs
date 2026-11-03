using System.Globalization;
using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>应用交付用例；先授权并保存意图，再在本地事务外处理文件和渲染。</summary>
internal sealed class EnterpriseRequestDeliveryService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    EnterpriseRequestDeliveryAccess access, EnterpriseRequestDeliveryProcessor processor,
    EnterpriseRequestDeliverySnapshotReader snapshots, EnterpriseRequestDocumentRenderer renderer, ITenantResourceFileStore files)
{
    internal const long MaxAttachmentBytes = 10 * 1024 * 1024;

    internal async Task<Result<EnterpriseRequestAttachmentResponse>> UploadAsync(Guid requestId, Guid key, long version,
        SessionBindingSnapshot binding, string fileName, string contentType, Stream content, long declaredLength, CancellationToken token)
    {
        var permission = await access.ReadAsync(requestId, binding,
            [EnterpriseRequestPermissions.Read, EnterpriseRequestDeliveryPermissions.AttachmentsManage], token).ConfigureAwait(false);
        if (!permission.IsSuccess) return Result<EnterpriseRequestAttachmentResponse>.Failure(permission.Error!);
        fileName = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        contentType = contentType.Trim().ToLowerInvariant();
        if (key == Guid.Empty || version < 1 || declaredLength is < 1 or > MaxAttachmentBytes || fileName.Length is < 1 or > 200
            || fileName.Any(char.IsControl) || contentType.Length is < 1 or > 128 || contentType.Any(c => c < 32 || c > 126))
            return Failure<EnterpriseRequestAttachmentResponse>("enterprise_request.attachment_invalid", ErrorType.Validation);
        using var buffered = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (buffered.Length > MaxAttachmentBytes - read) return Failure<EnterpriseRequestAttachmentResponse>("enterprise_request.attachment_limit_exceeded", ErrorType.Validation);
            await buffered.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        if (buffered.Length != declaredLength) return Failure<EnterpriseRequestAttachmentResponse>("enterprise_request.attachment_invalid", ErrorType.Validation);
        var hash = Convert.ToHexString(SHA256.HashData(buffered.GetBuffer().AsSpan(0, (int)buffered.Length)));
        buffered.Position = 0;
        var work = await FindKeyAsync(requestId, key, "Attachment", binding.UserId, token).ConfigureAwait(false);
        if (work is null)
        {
            var writable = await access.WriteAsync(requestId, binding, version, EnterpriseRequestDeliveryPermissions.AttachmentsManage, token).ConfigureAwait(false);
            if (!writable.IsSuccess)
            {
                // 同一请求可能刚完成绑定并推进主单版本；重读原身份后仍须核对完整文件摘要。
                work = await FindKeyAsync(requestId, key, "Attachment", binding.UserId, token).ConfigureAwait(false);
                if (work is null) return Result<EnterpriseRequestAttachmentResponse>.Failure(writable.Error!);
            }
            else
            {
                var created = await CreateIntentAsync(requestId, key, version, "Attachment", binding, hash, false,
                    fileName, contentType, buffered.Length, hash, clock.UtcNow.AddMinutes(15), token).ConfigureAwait(false);
                work = created.IsSuccess ? await processor.FindAsync(created.Value, token).ConfigureAwait(false)
                    : await FindKeyAsync(requestId, key, "Attachment", binding.UserId, token).ConfigureAwait(false);
                if (work is null) return Result<EnterpriseRequestAttachmentResponse>.Failure(created.Error!);
            }
        }
        if (work is null || work.RequestedVersion != version || work.FileName != fileName || work.ContentType != contentType
            || work.SizeBytes != buffered.Length || !string.Equals(work.SourceDigest, hash, StringComparison.OrdinalIgnoreCase))
            return Failure<EnterpriseRequestAttachmentResponse>("enterprise_request.delivery_request_conflict");
        if (work.Phase is not ("Uploading" or "Ready")) return Failure<EnterpriseRequestAttachmentResponse>("enterprise_request.delivery_state_invalid");
        if (work.Phase == "Uploading")
        {
            var outcome = await processor.ProcessAsync(work.Id, buffered, token).ConfigureAwait(false);
            if (!outcome.IsSuccess) return Result<EnterpriseRequestAttachmentResponse>.Failure(outcome.Error!);
            work = await processor.FindAsync(work.Id, token).ConfigureAwait(false) ?? throw new InvalidOperationException("Upload intent disappeared.");
        }
        return Result<EnterpriseRequestAttachmentResponse>.Success(work.Attachment());
    }

    internal async Task<Result<EnterpriseRequestReportResponse>> CreateReportAsync(Guid requestId, CreateEnterpriseRequestReportRequest request,
        SessionBindingSnapshot binding, CancellationToken token)
    {
        if (request.IdempotencyKey == Guid.Empty || request.RequestVersion < 1) return Failure<EnterpriseRequestReportResponse>("enterprise_request.delivery_invalid", ErrorType.Validation);
        var authorized = await access.ReadAsync(requestId, binding, EnterpriseRequestDeliveryProcessor.ReportPermissions, token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<EnterpriseRequestReportResponse>.Failure(authorized.Error!);
        var prior = await FindKeyAsync(requestId, request.IdempotencyKey, "Report", binding.UserId, token).ConfigureAwait(false);
        if (prior is not null) return prior.RequestedVersion == request.RequestVersion ? Result<EnterpriseRequestReportResponse>.Success(prior.Report())
            : Failure<EnterpriseRequestReportResponse>("enterprise_request.delivery_request_conflict");
        var source = await snapshots.ReadAsync(requestId, binding, EnterpriseRequestDeliveryProcessor.ReportPermissions,
            request.RequestVersion, null, token).ConfigureAwait(false);
        if (!source.IsSuccess)
        {
            prior = await FindKeyAsync(requestId, request.IdempotencyKey, "Report", binding.UserId, token).ConfigureAwait(false);
            return prior is not null && prior.RequestedVersion == request.RequestVersion
                ? Result<EnterpriseRequestReportResponse>.Success(prior.Report()) : Result<EnterpriseRequestReportResponse>.Failure(source.Error!);
        }
        var created = await CreateIntentAsync(requestId, request.IdempotencyKey, request.RequestVersion, "Report", binding,
            source.Value!.Digest(), source.Value.IncludesAmounts, "", EnterpriseRequestDeliveryProcessor.WorkbookType, null,
            null, clock.UtcNow.AddHours(24), token).ConfigureAwait(false);
        if (!created.IsSuccess)
        {
            prior = await FindKeyAsync(requestId, request.IdempotencyKey, "Report", binding.UserId, token).ConfigureAwait(false);
            return prior is not null && prior.RequestedVersion == request.RequestVersion
                ? Result<EnterpriseRequestReportResponse>.Success(prior.Report()) : Result<EnterpriseRequestReportResponse>.Failure(created.Error!);
        }
        return Result<EnterpriseRequestReportResponse>.Success((await processor.FindAsync(created.Value, token).ConfigureAwait(false))!.Report());
    }

    private Task<Result<Guid>> CreateIntentAsync(Guid requestId, Guid key, long version, string kind, SessionBindingSnapshot binding,
        string digest, bool amounts, string fileName, string contentType, long? size, string? hash, DateTimeOffset expires, CancellationToken token) =>
        transaction.ExecuteResultAsync(async local =>
        {
            var p = P(("RequestId", requestId), ("RequestedVersion", version), ("RequestKey", key), ("Kind", kind),
                ("ActorUserId", binding.UserId), ("Now", clock.UtcNow));
            if (await queries.QuerySingleOrDefaultAsync<long>(database.Value.Provider == DatabaseProvider.MySql
                    ? EnterpriseRequestDeliverySql.LockParentMySql : EnterpriseRequestDeliverySql.LockParentSqlServer, p, local).ConfigureAwait(false) == 0)
                return Failure<Guid>("enterprise_request.delivery_version_conflict");
            var previous = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestDeliveryRecord>(EnterpriseRequestDeliverySql.FindKey, p, local).ConfigureAwait(false);
            if (previous is not null) return previous.SourceDigest == digest && previous.RequestedVersion == version
                ? Result<Guid>.Success(previous.Id) : Failure<Guid>("enterprise_request.delivery_request_conflict");
            var count = await queries.QuerySingleOrDefaultAsync<long>(kind == "Attachment" ? EnterpriseRequestDeliverySql.CountAttachments
                : EnterpriseRequestDeliverySql.CountReports, p, local).ConfigureAwait(false);
            if (count >= (kind == "Attachment" ? 20 : 10)) return Failure<Guid>("enterprise_request.delivery_limit_exceeded", ErrorType.BusinessRule);
            var id = ids.NewId();
            p.Add("Id", id); p.Add("SessionId", binding.SessionId); p.Add("SecurityStamp", binding.SecurityStamp);
            p.Add("ActorScope", binding.ActorScope); p.Add("EffectiveScope", binding.EffectiveScope); p.Add("SessionKind", binding.SessionKind);
            p.Add("SourceDigest", digest); p.Add("IncludesAmounts", amounts); p.Add("LocaleCode", Locale());
            p.Add("FileName", kind == "Report" ? $"enterprise-request-{id:N}.xlsx" : fileName); p.Add("ContentType", contentType);
            p.Add("SizeBytes", size); p.Add("ContentHash", hash); p.Add("Phase", kind == "Report" ? "Queued" : "Uploading");
            p.Add("ExpiresAtUtc", expires);
            if (await commands.ExecuteAsync(EnterpriseRequestDeliverySql.Insert, p, local).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Delivery intent was not persisted.");
            return Result<Guid>.Success(id);
        }, token);

    internal async Task<Result<PagedResult<EnterpriseRequestAttachmentResponse>>> AttachmentsAsync(Guid requestId,
        SessionBindingSnapshot binding, int page, int size, CancellationToken token)
    {
        var authorized = await access.ReadAsync(requestId, binding, [EnterpriseRequestPermissions.Read,
            EnterpriseRequestDeliveryPermissions.AttachmentsRead], token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<PagedResult<EnterpriseRequestAttachmentResponse>>.Failure(authorized.Error!);
        var rows = await ListAsync(requestId, "Attachment", binding.UserId, page, size, token).ConfigureAwait(false);
        return Result<PagedResult<EnterpriseRequestAttachmentResponse>>.Success(new(rows.Items.Select(r => r.Attachment()).ToArray(), rows.Page, rows.PageSize, rows.Total));
    }

    internal async Task<Result<PagedResult<EnterpriseRequestReportResponse>>> ReportsAsync(Guid requestId,
        SessionBindingSnapshot binding, int page, int size, CancellationToken token)
    {
        var authorized = await access.ReadAsync(requestId, binding, [EnterpriseRequestPermissions.Read,
            EnterpriseRequestDeliveryPermissions.ReportsRead], token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<PagedResult<EnterpriseRequestReportResponse>>.Failure(authorized.Error!);
        var rows = await ListAsync(requestId, "Report", binding.UserId, page, size, token).ConfigureAwait(false);
        return Result<PagedResult<EnterpriseRequestReportResponse>>.Success(new(rows.Items.Select(r => r.Report()).ToArray(), rows.Page, rows.PageSize, rows.Total));
    }

    private async Task<PagedResult<EnterpriseRequestDeliveryRecord>> ListAsync(Guid requestId, string kind, Guid user,
        int page, int size, CancellationToken token)
    {
        page = Math.Max(1, page); size = Math.Clamp(size, 1, 100);
        var p = P(("RequestId", requestId), ("Kind", kind), ("ActorUserId", user), ("Offset", (long)(page - 1) * size), ("Take", size));
        var count = await queries.QuerySingleOrDefaultAsync<long>(EnterpriseRequestDeliverySql.CountRequest, p, token).ConfigureAwait(false);
        var rows = await queries.QueryAsync<EnterpriseRequestDeliveryRecord>(database.Value.Provider == DatabaseProvider.MySql
            ? EnterpriseRequestDeliverySql.ListRequestMySql : EnterpriseRequestDeliverySql.ListRequestSqlServer, p, token).ConfigureAwait(false);
        return new(rows, page, size, count);
    }

    internal async Task<Result<bool>> DeleteAttachmentAsync(Guid requestId, Guid id, DeleteEnterpriseRequestAttachmentRequest request,
        SessionBindingSnapshot binding, CancellationToken token)
    {
        var authorized = await access.ReadAsync(requestId, binding, [EnterpriseRequestPermissions.Read,
            EnterpriseRequestDeliveryPermissions.AttachmentsManage], token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<bool>.Failure(authorized.Error!);
        var work = await processor.FindAsync(id, token).ConfigureAwait(false);
        if (work is null || work.RequestId != requestId || work.Kind != "Attachment") return Failure<bool>("enterprise_request.delivery_not_found", ErrorType.NotFound);
        if (work.Phase is "Deleting" or "Deleted") return Result<bool>.Success(true);
        var writable = await access.WriteAsync(requestId, binding, request.RequestVersion, EnterpriseRequestDeliveryPermissions.AttachmentsManage, token).ConfigureAwait(false);
        if (!writable.IsSuccess) return Result<bool>.Failure(writable.Error!);
        return await transaction.ExecuteResultAsync(async local =>
        {
            var p = P(("Id", id), ("RequestId", requestId), ("RequestedVersion", request.RequestVersion),
                ("Version", request.AttachmentVersion), ("ActorUserId", binding.UserId), ("Now", clock.UtcNow));
            if (await queries.QuerySingleOrDefaultAsync<long>(database.Value.Provider == DatabaseProvider.MySql
                ? EnterpriseRequestDeliverySql.LockParentMySql : EnterpriseRequestDeliverySql.LockParentSqlServer, p, local).ConfigureAwait(false) == 0)
                return Failure<bool>("enterprise_request.delivery_version_conflict");
            if (await commands.ExecuteAsync(EnterpriseRequestDeliverySql.TouchParent, p, local).ConfigureAwait(false) != 1
                || await commands.ExecuteAsync(EnterpriseRequestDeliverySql.DeleteAttachment, p, local).ConfigureAwait(false) != 1)
                return Failure<bool>("enterprise_request.delivery_version_conflict");
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<EnterpriseRequestReportResponse>> CancelReportAsync(Guid requestId, Guid id,
        CancelEnterpriseRequestReportRequest request, SessionBindingSnapshot binding, CancellationToken token)
    {
        var authorized = await access.ReadAsync(requestId, binding, [EnterpriseRequestPermissions.Read,
            EnterpriseRequestDeliveryPermissions.ReportsRead, EnterpriseRequestDeliveryPermissions.ReportsCancel], token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<EnterpriseRequestReportResponse>.Failure(authorized.Error!);
        var work = await processor.FindAsync(id, token).ConfigureAwait(false);
        if (work is null || work.RequestId != requestId || work.Kind != "Report" || work.ActorUserId != binding.UserId)
            return Failure<EnterpriseRequestReportResponse>("enterprise_request.delivery_not_found", ErrorType.NotFound);
        if (work.Phase is "Cancelled" or "CancelledCleanup") return Result<EnterpriseRequestReportResponse>.Success(work.Report());
        if (await commands.ExecuteAsync(EnterpriseRequestDeliverySql.CancelReport, P(("Id", id), ("RequestId", requestId),
            ("ActorUserId", binding.UserId), ("Version", request.Version), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1)
            return Failure<EnterpriseRequestReportResponse>("enterprise_request.delivery_version_conflict");
        return Result<EnterpriseRequestReportResponse>.Success((await processor.FindAsync(id, token).ConfigureAwait(false))!.Report());
    }

    internal async Task<Result<TenantResourceFileContent>> DownloadAsync(Guid requestId, Guid id, string kind,
        SessionBindingSnapshot binding, CancellationToken token)
    {
        var permission = kind == "Attachment" ? EnterpriseRequestDeliveryPermissions.AttachmentsRead : EnterpriseRequestDeliveryPermissions.ReportsRead;
        var authorized = await access.ReadAsync(requestId, binding, [EnterpriseRequestPermissions.Read, permission], token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<TenantResourceFileContent>.Failure(authorized.Error!);
        var work = await processor.FindAsync(id, token).ConfigureAwait(false);
        if (work is null || work.RequestId != requestId || work.Kind != kind || work.Phase != "Ready" || work.FileId is null
            || (kind == "Report" && (work.ActorUserId != binding.UserId || work.ExpiresAtUtc <= clock.UtcNow)))
            return Failure<TenantResourceFileContent>("enterprise_request.delivery_not_found", ErrorType.NotFound);
        if (kind == "Report")
        {
            // 历史结果按当前下载会话复验；生成资格和原会话只约束 Worker，金额上限仍取任务快照。
            var source = await snapshots.ReadAsync(requestId, binding, [EnterpriseRequestDeliveryPermissions.ReportsRead],
                work.RequestedVersion, work.IncludesAmounts, token).ConfigureAwait(false);
            if (!source.IsSuccess) return Result<TenantResourceFileContent>.Failure(source.Error!);
            if (source.Value!.Digest() != work.SourceDigest) return Failure<TenantResourceFileContent>("enterprise_request.delivery_version_conflict");
        }
        var opened = await files.OpenReadyContentAsync(work.Owner, work.Id, work.FileId.Value, token).ConfigureAwait(false);
        if (!opened.IsSuccess) return opened;
        // 取消和删除可能发生在存储打开期间；交付前复核持久状态，不能只依赖先前的就绪快照。
        var current = await processor.FindAsync(id, token).ConfigureAwait(false);
        if (current is null || current.Phase != "Ready" || current.FileId != work.FileId
            || (kind == "Report" && current.ExpiresAtUtc <= clock.UtcNow))
        {
            await opened.Value!.Content.DisposeAsync().ConfigureAwait(false);
            return Failure<TenantResourceFileContent>("enterprise_request.delivery_not_found", ErrorType.NotFound);
        }
        // 文件打开耗时期间的会话/对象撤权仍须拒绝交付，拒绝时释放已打开的流。
        authorized = await access.ReadAsync(requestId, binding, [EnterpriseRequestPermissions.Read, permission], token).ConfigureAwait(false);
        if (!authorized.IsSuccess)
        {
            await opened.Value!.Content.DisposeAsync().ConfigureAwait(false);
            return Result<TenantResourceFileContent>.Failure(authorized.Error!);
        }
        if (kind == "Report")
        {
            var source = await snapshots.ReadAsync(requestId, binding, [EnterpriseRequestDeliveryPermissions.ReportsRead],
                work.RequestedVersion, work.IncludesAmounts, token).ConfigureAwait(false);
            if (!source.IsSuccess || source.Value!.Digest() != work.SourceDigest)
            {
                await opened.Value!.Content.DisposeAsync().ConfigureAwait(false);
                return source.IsSuccess ? Failure<TenantResourceFileContent>("enterprise_request.delivery_version_conflict")
                    : Result<TenantResourceFileContent>.Failure(source.Error!);
            }
        }
        return opened;
    }

    internal async Task<Result<EnterpriseRequestPrintResponse>> PrintAsync(Guid requestId, SessionBindingSnapshot binding, CancellationToken token)
    {
        var source = await snapshots.ReadAsync(requestId, binding, [EnterpriseRequestDeliveryPermissions.Print], null, null, token).ConfigureAwait(false);
        if (!source.IsSuccess) return Result<EnterpriseRequestPrintResponse>.Failure(source.Error!);
        var html = renderer.Print(source.Value!, Locale(), token);
        if (!html.IsSuccess) return Result<EnterpriseRequestPrintResponse>.Failure(html.Error!);
        var checkedSource = await snapshots.ReadAsync(requestId, binding, [EnterpriseRequestDeliveryPermissions.Print],
            source.Value!.Version, source.Value.IncludesAmounts, token).ConfigureAwait(false);
        if (!checkedSource.IsSuccess) return Result<EnterpriseRequestPrintResponse>.Failure(checkedSource.Error!);
        if (checkedSource.Value!.Digest() != source.Value.Digest()) return Failure<EnterpriseRequestPrintResponse>("enterprise_request.delivery_version_conflict");
        return Result<EnterpriseRequestPrintResponse>.Success(new(requestId, source.Value.Version, html.Value!, source.Value.IncludesAmounts, clock.UtcNow));
    }

    private Task<EnterpriseRequestDeliveryRecord?> FindKeyAsync(Guid requestId, Guid key, string kind, Guid user, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<EnterpriseRequestDeliveryRecord>(EnterpriseRequestDeliverySql.FindKey,
            P(("RequestId", requestId), ("RequestKey", key), ("Kind", kind), ("ActorUserId", user)), token);
    private static string Locale() => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
    private static Dictionary<string, object?> P(params (string Name, object? Value)[] pairs) => EnterpriseRequestDeliverySql.Parameters(pairs);
    private static Result<T> Failure<T>(string code, ErrorType type = ErrorType.Conflict) =>
        Result<T>.Failure(new Error(code, "The delivery request is not valid for the current object, version or authorization.", type));
}
