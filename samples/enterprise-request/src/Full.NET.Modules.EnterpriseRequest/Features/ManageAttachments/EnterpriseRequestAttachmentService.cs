using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.EnterpriseRequest.Features.ManageAttachments;

/// <summary>附件绑定使用申请本地事务；对象上传、下载和释放均通过 Files 端口在事务外完成。</summary>
internal sealed partial class EnterpriseRequestAttachmentService(EnterpriseRequestQueryService parents,
    IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction, ICurrentTenant tenant,
    IOrganizationOwnedEntityWriteAuthorizer authorizer, ITenantResourceFileStore files,
    IClock clock, ILogger<EnterpriseRequestAttachmentService> logger)
{
    internal const int MaximumAttachments = 20;
    internal const long MaximumBytes = 10 * 1024 * 1024;
    private const string Owner = "enterprise_request";
    public async Task<Result<EnterpriseRequestAttachmentsResponse>> ListAsync(Guid id, Guid actor, bool super,
        CancellationToken ct = default)
    {
        var parent = await ReadParentAsync(id, actor, super, ct).ConfigureAwait(false);
        if (!parent.IsSuccess) return Result<EnterpriseRequestAttachmentsResponse>.Failure(parent.Error!);
        var rows = await queries.QueryAsync<EnterpriseRequestAttachmentRow>(EnterpriseRequestAttachmentSql.List,
            Parameters(("RequestId", id)), ct).ConfigureAwait(false);
        var current = await ReadParentAsync(id, actor, super, ct).ConfigureAwait(false);
        if (!current.IsSuccess) return Result<EnterpriseRequestAttachmentsResponse>.Failure(current.Error!);
        if (!Same(parent.Value!, current.Value!) || rows.Count > MaximumAttachments || rows.Any(row => !Owned(row, id)))
            return Result<EnterpriseRequestAttachmentsResponse>.Failure(Conflict());
        return Result<EnterpriseRequestAttachmentsResponse>.Success(new(id, current.Value!.Version, current.Value.Status,
            rows.Select(Response).ToArray()));
    }

    public async Task<Result<EnterpriseRequestAttachmentMutationResponse>> UploadAsync(Guid id, long version,
        Guid actor, string name, Stream content, long length, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var parent = await WriteParentAsync(id, version, actor, ct).ConfigureAwait(false);
        if (!parent.IsSuccess) return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(parent.Error!);
        var fileName = Path.GetFileName((name ?? string.Empty).Replace('\\', '/')).Trim();
        if (fileName.Length is < 1 or > 255 || fileName.Any(char.IsControl) || fileName is "." or ".." || length is <= 0 or > MaximumBytes)
            return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(Invalid());
        // 独立验证实际长度和上限，不让伪造 multipart 长度在进入 Files 前占用无限缓冲。
        using var buffer = new MemoryStream(); var chunk = new byte[81920]; int read;
        while ((read = await content.ReadAsync(chunk, ct).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length > MaximumBytes - read || buffer.Length > length - read)
                return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(Invalid());
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != length) return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(Invalid());
        buffer.Position = 0;
        var uploaded = await files.UploadAsync(Owner, id, actor, fileName, "application/octet-stream", buffer, length, ct).ConfigureAwait(false);
        if (!uploaded.IsSuccess) return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(uploaded.Error!);
        var reference = uploaded.Value!; var now = clock.UtcNow;
        var intent = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestAttachmentRow>(EnterpriseRequestAttachmentSql.FindUpload,
            Parameters(("RequestId", id), ("FileId", reference.FileId)), ct).ConfigureAwait(false);
        if (intent is null || !Owned(intent, id) || intent.FileId != reference.FileId)
        {
            await ReleaseAsync(id, reference.FileId, ct).ConfigureAwait(false);
            return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(Conflict());
        }
        // 未知提交结果不主动删文件，避免提交已成功后误删；精确 owner 对账最终回收未绑定文件。
        var result = await transaction.ExecuteResultAsync(async token => {
            if (!await AdvanceAsync(id, version, actor, parent.Value!, now, token).ConfigureAwait(false))
                return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(Conflict());
            if (await queries.QuerySingleOrDefaultAsync<int>(EnterpriseRequestAttachmentSql.Count, Parameters(("RequestId", id)), token).ConfigureAwait(false) >= MaximumAttachments)
                return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(Invalid());
            if (await commands.ExecuteAsync(EnterpriseRequestAttachmentSql.Bind, Parameters(("Id", intent.Id),
                    ("RequestId", id), ("FileId", reference.FileId)), token).ConfigureAwait(false) != 1)
                return Result<EnterpriseRequestAttachmentMutationResponse>.Failure(Conflict());
            return Result<EnterpriseRequestAttachmentMutationResponse>.Success(new(id, version + 1,
                Response(intent)));
        }, ct).ConfigureAwait(false);
        if (!result.IsSuccess) await ReleaseAsync(id, reference.FileId, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<EnterpriseRequestAttachmentRemovedResponse>> RemoveAsync(Guid id, Guid attachmentId,
        long version, Guid actor, CancellationToken ct = default)
    {
        var parent = await WriteParentAsync(id, version, actor, ct).ConfigureAwait(false);
        if (!parent.IsSuccess) return Result<EnterpriseRequestAttachmentRemovedResponse>.Failure(parent.Error!);
        var row = await FindAsync(id, attachmentId, ct).ConfigureAwait(false);
        if (row is null || !Owned(row, id)) return Result<EnterpriseRequestAttachmentRemovedResponse>.Failure(NotFound());
        var result = await transaction.ExecuteResultAsync(async token => {
            if (!await AdvanceAsync(id, version, actor, parent.Value!, clock.UtcNow, token).ConfigureAwait(false))
                return Result<EnterpriseRequestAttachmentRemovedResponse>.Failure(Conflict());
            if (await commands.ExecuteAsync(EnterpriseRequestAttachmentSql.Delete, Parameters(("RequestId", id),
                ("Id", attachmentId), ("FileId", row.FileId)), token).ConfigureAwait(false) != 1)
                return Result<EnterpriseRequestAttachmentRemovedResponse>.Failure(Conflict());
            return Result<EnterpriseRequestAttachmentRemovedResponse>.Success(new(id, version + 1));
        }, ct).ConfigureAwait(false);
        if (result.IsSuccess) await ReleaseAsync(id, row.FileId, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<TenantResourceFileContent>> OpenAsync(Guid id, Guid attachmentId, Guid actor, bool super,
        CancellationToken ct = default)
    {
        var parent = await ReadParentAsync(id, actor, super, ct).ConfigureAwait(false);
        if (!parent.IsSuccess) return Result<TenantResourceFileContent>.Failure(parent.Error!);
        var row = await FindAsync(id, attachmentId, ct).ConfigureAwait(false);
        if (row is null || !Owned(row, id)) return Result<TenantResourceFileContent>.Failure(NotFound());
        var opened = await files.OpenReadyContentAsync(Owner, id, row.FileId, ct).ConfigureAwait(false);
        if (!opened.IsSuccess) return opened;
        try
        {
            // 文件 I/O 之后重新核对数据范围与版本；删除、提交或机构移动不能接入旧快照。
            var current = await ReadParentAsync(id, actor, super, ct).ConfigureAwait(false);
            if (current.IsSuccess && Same(parent.Value!, current.Value!)) return opened;
            await opened.Value!.Content.DisposeAsync().ConfigureAwait(false);
            return Result<TenantResourceFileContent>.Failure(current.IsSuccess ? Conflict() : current.Error!);
        }
        catch { await opened.Value!.Content.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task<Result<EnterpriseRequestResponse>> ReadParentAsync(Guid id, Guid actor, bool super, CancellationToken ct)
    {
        if (!Context(id, actor)) return Result<EnterpriseRequestResponse>.Failure(NotFound());
        var result = await parents.GetByIdAsync(id, actor, super, ct).ConfigureAwait(false);
        return result.IsSuccess && !Valid(result.Value!, id) ? Result<EnterpriseRequestResponse>.Failure(NotFound()) : result;
    }
    private async Task<Result<EnterpriseRequestResponse>> WriteParentAsync(Guid id, long version, Guid actor, CancellationToken ct)
    {
        if (!Context(id, actor)) return Result<EnterpriseRequestResponse>.Failure(NotFound());
        var result = await parents.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (!result.IsSuccess) return result;
        var parent = result.Value!;
        if (!Valid(parent, id)) return Result<EnterpriseRequestResponse>.Failure(NotFound());
        var allowed = await authorizer.EnsureCanWriteAsync(tenant.Id!.Value, parent.OrganizationUnitId, actor, ct).ConfigureAwait(false);
        if (!allowed.IsSuccess) return Result<EnterpriseRequestResponse>.Failure(allowed.Error!);
        if (!allowed.Value) return Result<EnterpriseRequestResponse>.Failure(new Error(OrganizationErrorCodes.WriteAccessDenied, "The organization unit was denied.", ErrorType.Forbidden));
        if (version <= 0 || version == long.MaxValue || version != parent.Version) return Result<EnterpriseRequestResponse>.Failure(Conflict());
        if (parent.Status != EnterpriseRequestStatusKeys.Draft) return Result<EnterpriseRequestResponse>.Failure(new Error(
            EnterpriseRequestWorkflowErrorCodes.InvalidStatus, "Only draft request attachments can be changed.", ErrorType.Conflict));
        return result;
    }
    private Task<EnterpriseRequestAttachmentRow?> FindAsync(Guid id, Guid attachmentId, CancellationToken ct) =>
        queries.QuerySingleOrDefaultAsync<EnterpriseRequestAttachmentRow>(EnterpriseRequestAttachmentSql.Find,
            Parameters(("RequestId", id), ("Id", attachmentId)), ct);
    private async Task<bool> AdvanceAsync(Guid id, long version, Guid actor, EnterpriseRequestResponse parent, DateTimeOffset now, CancellationToken ct) =>
        await commands.ExecuteAsync(EnterpriseRequestAttachmentSql.AdvanceParent, Parameters(("RequestId", id), ("Version", version),
            ("OrganizationUnitId", parent.OrganizationUnitId), ("ExpectedStatus", EnterpriseRequestStatusKeys.Draft), ("Now", now), ("ActorId", actor)), ct).ConfigureAwait(false) == 1;
    private async Task ReleaseAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        try { await files.ReleaseAsync(Owner, id, fileId, ct).ConfigureAwait(false); }
        catch (Exception) { CleanupDeferred(logger, id, fileId); }
    }
    [LoggerMessage(1, LogLevel.Warning, "申请 {RequestId} 文件 {FileId} 的释放延后，由 Files 归属对账重试。")]
    private static partial void CleanupDeferred(ILogger logger, Guid requestId, Guid fileId);
    private bool Context(Guid id, Guid actor) => tenant.IsAvailable && !tenant.IsHost && tenant.Id is { } tenantId && tenantId != Guid.Empty && id != Guid.Empty && actor != Guid.Empty;
    private bool Valid(EnterpriseRequestResponse parent, Guid id) => parent.Id == id && parent.TenantId == tenant.Id && !parent.IsDeleted;
    private bool Owned(EnterpriseRequestAttachmentRow row, Guid id) => row.TenantId == tenant.Id && row.RequestId == id && row.Id != Guid.Empty && row.FileId != Guid.Empty;
    private static bool Same(EnterpriseRequestResponse left, EnterpriseRequestResponse right) => left.Version == right.Version && left.OrganizationUnitId == right.OrganizationUnitId && left.Status == right.Status;
    private static EnterpriseRequestAttachmentResponse Response(EnterpriseRequestAttachmentRow row) => new(row.Id, row.FileId, row.OriginalFileName, row.SizeBytes, row.CreatedAtUtc);
    private static Error NotFound() => new(EnterpriseRequestErrorCodes.NotFound, "The attachment or request was not found.", ErrorType.NotFound);
    private static Error Conflict() => new(EnterpriseRequestErrorCodes.VersionConflict, "The request snapshot changed. Refresh before retrying.", ErrorType.Conflict);
    private static Error Invalid() => new(ValidationErrorCodes.Failed, "At most 20 attachments, each containing 1 byte to 10 MiB, can be bound to a draft request.", ErrorType.Validation);
    private static Dictionary<string, object?> Parameters(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
