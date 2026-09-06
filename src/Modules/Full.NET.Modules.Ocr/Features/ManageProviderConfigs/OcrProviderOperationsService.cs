using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ocr.Connectivity;
using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Persistence;
using Full.NET.Modules.Ocr.Security;

namespace Full.NET.Modules.Ocr.Features.ManageProviderConfigs;

internal sealed class OcrProviderOperationsService(
    ICommandExecutor commandExecutor,
    OcrProviderQueryService queries,
    OcrApiKeyProtector apiKeyProtector,
    PaddleOcrIdCardClient paddleOcrIdCardClient,
    IClock clock)
{
    public async Task<Result<TestOcrProviderConfigResult>> TestAsync(
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var record = await queries.FindRecordByKeyAsync(providerKey, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result<TestOcrProviderConfigResult>.Failure(new Error(
                OcrErrorCodes.ProviderNotFound,
                "The OCR provider configuration was not found.",
                ErrorType.NotFound));
        }

        var apiKey = string.IsNullOrWhiteSpace(record.ApiKeyProtected)
            ? null
            : apiKeyProtector.Unprotect(record.ApiKeyProtected);
        var outcome = await paddleOcrIdCardClient.TestAsync(record, apiKey, cancellationToken).ConfigureAwait(false);
        var statusKey = outcome.Succeeded ? "succeeded" : "failed";
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                OcrProviderSql.UpdateTestResult,
                OcrSqlParameters.Create([
                    ("Id", record.Id),
                    ("LastTestedAtUtc", now),
                    ("LastTestStatusKey", statusKey),
                    ("LastTestMessage", outcome.Message),
                    ("UpdatedAtUtc", now),
                    ("Version", record.Version)]),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<TestOcrProviderConfigResult>.Success(
            new TestOcrProviderConfigResult(outcome.Succeeded, outcome.Message));
    }
}

internal sealed class OcrProviderSecretResolver(OcrApiKeyProtector apiKeyProtector)
{
    public string? ResolveApiKey(OcrProviderConfigRecord record) =>
        string.IsNullOrWhiteSpace(record.ApiKeyProtected)
            ? null
            : apiKeyProtector.Unprotect(record.ApiKeyProtected);
}
