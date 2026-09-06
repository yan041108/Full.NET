using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Persistence;
using Full.NET.Modules.Ocr.Security;

namespace Full.NET.Modules.Ocr.Features.ManageProviderConfigs;

internal sealed class OcrProviderManagementService(
    ICommandExecutor commandExecutor,
    OcrProviderQueryService queries,
    OcrApiKeyProtector apiKeyProtector,
    IClock clock)
{
    public async Task<Result<OcrProviderConfigResponse>> UpdateAsync(
        string providerKey,
        UpdateOcrProviderConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.BaseUrl)
            || !Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            return ValidationFailure("Provider name and absolute base URL are required.");
        }

        var record = await queries.FindRecordByKeyAsync(providerKey, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.Version != request.Version)
        {
            return VersionConflict();
        }

        var apiKeyProtected = string.IsNullOrWhiteSpace(request.ApiKey)
            ? record.ApiKeyProtected
            : apiKeyProtector.Protect(request.ApiKey.Trim());
        var affected = await commandExecutor.ExecuteAsync(
                OcrProviderSql.Update,
                OcrSqlParameters.Create([
                    ("Id", record.Id),
                    ("Name", request.Name.Trim()),
                    ("BaseUrl", request.BaseUrl.Trim()),
                    ("ApiKeyProtected", apiKeyProtected),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return VersionConflict();
        }

        return await queries.GetByKeyAsync(providerKey, cancellationToken).ConfigureAwait(false);
    }

    private static Result<OcrProviderConfigResponse> NotFound() =>
        Result<OcrProviderConfigResponse>.Failure(new Error(
            OcrErrorCodes.ProviderNotFound,
            "The OCR provider configuration was not found.",
            ErrorType.NotFound));

    private static Result<OcrProviderConfigResponse> VersionConflict() =>
        Result<OcrProviderConfigResponse>.Failure(new Error(
            OcrErrorCodes.ProviderInvalid,
            "The OCR provider configuration was modified by another request.",
            ErrorType.Conflict));

    private static Result<OcrProviderConfigResponse> ValidationFailure(string message) =>
        Result<OcrProviderConfigResponse>.Failure(new Error(
            OcrErrorCodes.ProviderInvalid,
            message,
            ErrorType.Validation));
}
