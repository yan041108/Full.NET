using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Domain;
using Full.NET.Modules.Platform.Persistence;

namespace Full.NET.Modules.Platform.Features.ManageHostReleaseNotes;

/// <summary>Host 更新日志创建、更新、发布、撤回与草稿删除；状态推进使用 CAS 并发控制。</summary>
internal sealed class HostReleaseNoteManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostReleaseNoteQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>版本标签允许的最大字符数。</summary>
    internal const int MaxVersionLabelLength = 64;

    /// <summary>标题允许的最大字符数。</summary>
    internal const int MaxTitleLength = 200;

    /// <summary>创建一条处于草稿状态的 Host 更新日志。</summary>
    /// <param name="actorUserId">操作人用户标识。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建更新日志或稳定业务错误。</returns>
    public Task<Result<HostReleaseNoteResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostReleaseNoteRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    /// <summary>更新未发布草稿更新日志；使用乐观版本号做 CAS 并发控制。</summary>
    /// <param name="actorUserId">操作人用户标识。</param>
    /// <param name="releaseNoteId">更新日志标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的更新日志或稳定业务错误。</returns>
    public Task<Result<HostReleaseNoteResponse>> UpdateAsync(
        Guid actorUserId,
        Guid releaseNoteId,
        UpdateHostReleaseNoteRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(actorUserId, releaseNoteId, request, token),
            cancellationToken);

    /// <summary>发布草稿更新日志；已发布时幂等返回当前事实。</summary>
    /// <param name="actorUserId">操作人用户标识。</param>
    /// <param name="releaseNoteId">更新日志标识。</param>
    /// <param name="version">客户端感知的乐观并发版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发布后的更新日志或稳定业务错误。</returns>
    public Task<Result<HostReleaseNoteResponse>> PublishAsync(
        Guid actorUserId,
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => PublishCoreAsync(actorUserId, releaseNoteId, version, token),
            cancellationToken);

    /// <summary>撤回已发布更新日志；已撤回时幂等返回当前事实。</summary>
    /// <param name="actorUserId">操作人用户标识。</param>
    /// <param name="releaseNoteId">更新日志标识。</param>
    /// <param name="version">客户端感知的乐观并发版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>撤回后的更新日志或稳定业务错误。</returns>
    public Task<Result<HostReleaseNoteResponse>> RetractAsync(
        Guid actorUserId,
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => RetractCoreAsync(actorUserId, releaseNoteId, version, token),
            cancellationToken);

    /// <summary>删除草稿更新日志；仅草稿可删除。</summary>
    /// <param name="releaseNoteId">更新日志标识。</param>
    /// <param name="version">客户端感知的乐观并发版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public Task<Result<bool>> DeleteAsync(
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DeleteCoreAsync(releaseNoteId, version, token),
            cancellationToken);

    /// <summary>校验版本标签并返回规范化文本与排序键。</summary>
    /// <param name="versionLabel">原始版本标签。</param>
    /// <returns>校验结果。</returns>
    internal static Result<(string VersionLabel, long VersionSortKey)> ValidateVersionLabel(
        string? versionLabel)
    {
        var normalized = versionLabel?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > MaxVersionLabelLength)
        {
            return Result<(string, long)>.Failure(new Error(
                PlatformErrorCodes.ReleaseNoteValidationFailed,
                "Release note version label must be between 1 and 64 characters.",
                ErrorType.Validation));
        }

        if (!ReleaseNoteVersionSortKey.TryParse(normalized, out var sortKey))
        {
            return Result<(string, long)>.Failure(new Error(
                PlatformErrorCodes.ReleaseNoteInvalidVersionLabel,
                "Release note version label is invalid.",
                ErrorType.Validation));
        }

        return Result<(string, long)>.Success((normalized, sortKey));
    }

    /// <summary>校验草稿标题与正文。</summary>
    /// <param name="title">标题。</param>
    /// <param name="content">正文。</param>
    /// <returns>校验通过时返回规范化文本，否则返回稳定业务错误。</returns>
    internal static Result<(string Title, string Content)> ValidateDraftContent(
        string? title,
        string? content)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedContent = content?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > MaxTitleLength)
        {
            return Result<(string, string)>.Failure(new Error(
                PlatformErrorCodes.ReleaseNoteValidationFailed,
                "Release note title must be between 1 and 200 characters.",
                ErrorType.Validation));
        }

        if (normalizedContent.Length < 1)
        {
            return Result<(string, string)>.Failure(new Error(
                PlatformErrorCodes.ReleaseNoteValidationFailed,
                "Release note content must not be empty.",
                ErrorType.Validation));
        }

        return Result<(string, string)>.Success((normalizedTitle, normalizedContent));
    }

    private async Task<Result<HostReleaseNoteResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateHostReleaseNoteRequest request,
        CancellationToken cancellationToken)
    {
        var contentValidation = ValidateDraftContent(request.Title, request.Content);
        if (!contentValidation.IsSuccess)
        {
            return Result<HostReleaseNoteResponse>.Failure(contentValidation.Error!);
        }

        var versionValidation = ValidateVersionLabel(request.VersionLabel);
        if (!versionValidation.IsSuccess)
        {
            return Result<HostReleaseNoteResponse>.Failure(versionValidation.Error!);
        }

        var duplicate = await queryExecutor.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                ReleaseNoteSql.FindHostByVersionLabel,
                ReleaseNoteSqlParameters.Create(
                    ("VersionLabel", versionValidation.Value!.VersionLabel)),
                cancellationToken)
            .ConfigureAwait(false);
        if (duplicate is not null)
        {
            return VersionLabelConflict();
        }

        var now = clock.UtcNow;
        var releaseNoteId = idGenerator.NewId();
        try
        {
            await commandExecutor.ExecuteAsync(
                    ReleaseNoteSql.Insert,
                    ReleaseNoteSqlParameters.Create(
                        ("Id", releaseNoteId),
                        ("VersionLabel", versionValidation.Value.VersionLabel),
                        ("VersionSortKey", versionValidation.Value.VersionSortKey),
                        ("Title", contentValidation.Value!.Title),
                        ("Content", contentValidation.Value.Content),
                        ("Status", ReleaseNoteStatuses.Draft),
                        ("CreatedAtUtc", now),
                        ("CreatedByUserId", actorUserId),
                        ("Version", 1)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUniqueVersionLabelViolation(exception))
        {
            return VersionLabelConflict();
        }

        return await queries.GetByIdAsync(releaseNoteId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostReleaseNoteResponse>> UpdateCoreAsync(
        Guid actorUserId,
        Guid releaseNoteId,
        UpdateHostReleaseNoteRequest request,
        CancellationToken cancellationToken)
    {
        var contentValidation = ValidateDraftContent(request.Title, request.Content);
        if (!contentValidation.IsSuccess)
        {
            return Result<HostReleaseNoteResponse>.Failure(contentValidation.Error!);
        }

        var versionValidation = ValidateVersionLabel(request.VersionLabel);
        if (!versionValidation.IsSuccess)
        {
            return Result<HostReleaseNoteResponse>.Failure(versionValidation.Error!);
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                ReleaseNoteSql.FindHostById,
                ReleaseNoteSqlParameters.Create(("Id", releaseNoteId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!string.Equals(existing.Status, ReleaseNoteStatuses.Draft, StringComparison.Ordinal))
        {
            return InvalidStatus("Only draft release notes can be updated.");
        }

        if (!string.Equals(
                existing.VersionLabel,
                versionValidation.Value!.VersionLabel,
                StringComparison.Ordinal))
        {
            var duplicate = await queryExecutor.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                    ReleaseNoteSql.FindHostByVersionLabel,
                    ReleaseNoteSqlParameters.Create(
                        ("VersionLabel", versionValidation.Value.VersionLabel)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (duplicate is not null && duplicate.Id != releaseNoteId)
            {
                return VersionLabelConflict();
            }
        }

        var now = clock.UtcNow;
        try
        {
            var affected = await commandExecutor.ExecuteAsync(
                    ReleaseNoteSql.UpdateDraft,
                    ReleaseNoteSqlParameters.Create(
                        ("Id", releaseNoteId),
                        ("VersionLabel", versionValidation.Value.VersionLabel),
                        ("VersionSortKey", versionValidation.Value.VersionSortKey),
                        ("Title", contentValidation.Value!.Title),
                        ("Content", contentValidation.Value.Content),
                        ("UpdatedAtUtc", now),
                        ("UpdatedByUserId", actorUserId),
                        ("NextVersion", request.Version + 1),
                        ("DraftStatus", ReleaseNoteStatuses.Draft),
                        ("Version", request.Version)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected == 0)
            {
                return ConcurrencyConflict();
            }
        }
        catch (Exception exception) when (IsUniqueVersionLabelViolation(exception))
        {
            return VersionLabelConflict();
        }

        return await queries.GetByIdAsync(releaseNoteId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostReleaseNoteResponse>> PublishCoreAsync(
        Guid actorUserId,
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                ReleaseNoteSql.FindHostById,
                ReleaseNoteSqlParameters.Create(("Id", releaseNoteId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (string.Equals(existing.Status, ReleaseNoteStatuses.Published, StringComparison.Ordinal))
        {
            return await queries.GetByIdAsync(releaseNoteId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.Equals(existing.Status, ReleaseNoteStatuses.Draft, StringComparison.Ordinal))
        {
            return InvalidStatus("Only draft release notes can be published.");
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                ReleaseNoteSql.Publish,
                ReleaseNoteSqlParameters.Create(
                    ("Id", releaseNoteId),
                    ("PublishedStatus", ReleaseNoteStatuses.Published),
                    ("DraftStatus", ReleaseNoteStatuses.Draft),
                    ("PublishedAtUtc", now),
                    ("PublishedByUserId", actorUserId),
                    ("UpdatedAtUtc", now),
                    ("UpdatedByUserId", actorUserId),
                    ("NextVersion", version + 1),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        return await queries.GetByIdAsync(releaseNoteId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostReleaseNoteResponse>> RetractCoreAsync(
        Guid actorUserId,
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                ReleaseNoteSql.FindHostById,
                ReleaseNoteSqlParameters.Create(("Id", releaseNoteId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (string.Equals(existing.Status, ReleaseNoteStatuses.Retracted, StringComparison.Ordinal))
        {
            return await queries.GetByIdAsync(releaseNoteId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.Equals(existing.Status, ReleaseNoteStatuses.Published, StringComparison.Ordinal))
        {
            return InvalidStatus("Only published release notes can be retracted.");
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                ReleaseNoteSql.Retract,
                ReleaseNoteSqlParameters.Create(
                    ("Id", releaseNoteId),
                    ("RetractedStatus", ReleaseNoteStatuses.Retracted),
                    ("PublishedStatus", ReleaseNoteStatuses.Published),
                    ("RetractedAtUtc", now),
                    ("RetractedByUserId", actorUserId),
                    ("UpdatedAtUtc", now),
                    ("UpdatedByUserId", actorUserId),
                    ("NextVersion", version + 1),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        return await queries.GetByIdAsync(releaseNoteId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid releaseNoteId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                ReleaseNoteSql.FindHostById,
                ReleaseNoteSqlParameters.Create(("Id", releaseNoteId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return Result<bool>.Failure(new Error(
                PlatformErrorCodes.ReleaseNoteNotFound,
                "The release note was not found.",
                ErrorType.NotFound));
        }

        if (!string.Equals(existing.Status, ReleaseNoteStatuses.Draft, StringComparison.Ordinal))
        {
            return Result<bool>.Failure(new Error(
                PlatformErrorCodes.ReleaseNoteInvalidStatus,
                "Only draft release notes can be deleted.",
                ErrorType.Validation));
        }

        var affected = await commandExecutor.ExecuteAsync(
                ReleaseNoteSql.DeleteDraft,
                ReleaseNoteSqlParameters.Create(
                    ("Id", releaseNoteId),
                    ("DraftStatus", ReleaseNoteStatuses.Draft),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<bool>.Failure(new Error(
                PlatformErrorCodes.ReleaseNoteConcurrencyConflict,
                "The release note changed concurrently.",
                ErrorType.Conflict));
        }

        return Result<bool>.Success(true);
    }

    private static bool IsUniqueVersionLabelViolation(Exception exception) =>
        exception.Message.Contains("UX_fn_platform_release_note_VersionLabel", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);

    private static Result<HostReleaseNoteResponse> NotFound() =>
        Result<HostReleaseNoteResponse>.Failure(new Error(
            PlatformErrorCodes.ReleaseNoteNotFound,
            "The release note was not found.",
            ErrorType.NotFound));

    private static Result<HostReleaseNoteResponse> ConcurrencyConflict() =>
        Result<HostReleaseNoteResponse>.Failure(new Error(
            PlatformErrorCodes.ReleaseNoteConcurrencyConflict,
            "The release note changed concurrently.",
            ErrorType.Conflict));

    private static Result<HostReleaseNoteResponse> InvalidStatus(string message) =>
        Result<HostReleaseNoteResponse>.Failure(new Error(
            PlatformErrorCodes.ReleaseNoteInvalidStatus,
            message,
            ErrorType.Validation));

    private static Result<HostReleaseNoteResponse> VersionLabelConflict() =>
        Result<HostReleaseNoteResponse>.Failure(new Error(
            PlatformErrorCodes.ReleaseNoteVersionLabelConflict,
            "A release note with the same version label already exists.",
            ErrorType.Conflict));
}
