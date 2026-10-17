using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.UpdateHostDocumentVersionRetention;

internal sealed class DocumentVersionRetentionSettingService(
    DocumentVersionRetentionSettingRepository repository,
    DocumentVersionRetentionSettingsBootstrap bootstrap,
    IOptionsMonitor<DocumentVersionRetentionOptions> optionsMonitor)
{
    public async Task<HostDocumentVersionRetentionSettingsResponse> GetEffectiveAsync(CancellationToken cancellationToken)
    {
        // 版本凭据与字段必须来自同一次权威读取，不能拼接进程缓存与数据库版本。
        var record = await repository.GetHostAsync(cancellationToken).ConfigureAwait(false);
        return record is null ? Map(optionsMonitor.CurrentValue) : new(
            record.MinimumRetainedVersionsPerItem,
            record.MaximumRetainedHistoryVersions,
            record.PollSeconds,
            record.BatchSize) { Version = record.Version };
    }

    public async Task<Result<HostDocumentVersionRetentionSettingsResponse>> UpdateAsync(
        UpdateHostDocumentVersionRetentionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.IsSuccess)
        {
            return Result<HostDocumentVersionRetentionSettingsResponse>.Failure(validation.Error!);
        }

        var saved = await repository
            .TrySaveHostAsync(
                request.MinimumRetainedVersionsPerItem,
                request.MaximumRetainedHistoryVersions,
                request.PollSeconds,
                request.BatchSize,
                request.Version,
                cancellationToken)
            .ConfigureAwait(false);

        if (!saved)
        {
            return Result<HostDocumentVersionRetentionSettingsResponse>.Failure(new Error(
                DocumentErrorCodes.VersionRetentionConflict,
                "The document version retention settings have changed. Refresh before saving again.",
                ErrorType.Conflict));
        }

        await bootstrap.ReloadAsync(cancellationToken).ConfigureAwait(false);

        // 返回本次提交快照，避免并发管理员的后续写入混入成功响应。
        return Result<HostDocumentVersionRetentionSettingsResponse>.Success(new(
            request.MinimumRetainedVersionsPerItem,
            request.MaximumRetainedHistoryVersions,
            request.PollSeconds,
            request.BatchSize) { Version = request.Version + 1 });
    }

    private static Result<bool> Validate(UpdateHostDocumentVersionRetentionRequest request)
    {
        if (request.Version < 0 || request.Version == long.MaxValue)
        {
            return Result<bool>.Failure(new Error(
                DocumentErrorCodes.VersionRetentionInvalid, "The settings version is invalid.", ErrorType.Validation));
        }

        var options = new DocumentVersionRetentionOptions
        {
            MinimumRetainedVersionsPerItem = request.MinimumRetainedVersionsPerItem,
            MaximumRetainedHistoryVersions = request.MaximumRetainedHistoryVersions,
            PollSeconds = request.PollSeconds,
            BatchSize = request.BatchSize,
        };

        var validator = new DocumentVersionRetentionOptionsValidator();
        var result = validator.Validate(null, options);
        if (result.Failed)
        {
            return Result<bool>.Failure(
                new Error(
                    DocumentErrorCodes.VersionRetentionInvalid,
                    string.Join(' ', result.Failures),
                    ErrorType.Validation));
        }

        return Result<bool>.Success(true);
    }

    private static HostDocumentVersionRetentionSettingsResponse Map(DocumentVersionRetentionOptions options) =>
        new(
            options.MinimumRetainedVersionsPerItem,
            options.MaximumRetainedHistoryVersions,
            options.PollSeconds,
            options.BatchSize);
}
