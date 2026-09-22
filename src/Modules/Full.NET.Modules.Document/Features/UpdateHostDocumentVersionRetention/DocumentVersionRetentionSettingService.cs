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
    public HostDocumentVersionRetentionSettingsResponse GetEffective() => Map(optionsMonitor.CurrentValue);

    public async Task<Result<HostDocumentVersionRetentionSettingsResponse>> UpdateAsync(
        UpdateHostDocumentVersionRetentionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.IsSuccess)
        {
            return Result<HostDocumentVersionRetentionSettingsResponse>.Failure(validation.Error!);
        }

        await repository
            .UpsertHostAsync(
                request.MinimumRetainedVersionsPerItem,
                request.MaximumRetainedHistoryVersions,
                request.PollSeconds,
                request.BatchSize,
                cancellationToken)
            .ConfigureAwait(false);

        await bootstrap.ReloadAsync(cancellationToken).ConfigureAwait(false);

        return Result<HostDocumentVersionRetentionSettingsResponse>.Success(Map(optionsMonitor.CurrentValue));
    }

    private static Result<bool> Validate(UpdateHostDocumentVersionRetentionRequest request)
    {
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
                    "document.version_retention.invalid",
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
