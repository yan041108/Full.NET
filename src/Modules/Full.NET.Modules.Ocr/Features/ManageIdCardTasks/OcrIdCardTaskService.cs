using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Ocr.Connectivity;
using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Domain;
using Full.NET.Modules.Ocr.Features.ManageProviderConfigs;
using Full.NET.Modules.Ocr.Persistence;

namespace Full.NET.Modules.Ocr.Features.ManageIdCardTasks;

/// <summary>创建、识别、确认与拒绝身份证 OCR 任务。</summary>
internal sealed class OcrIdCardTaskService(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OcrIdCardTaskQueryService queries,
    OcrProviderQueryService providerQueries,
    OcrProviderSecretResolver providerSecrets,
    IHostFileDescriptorReader hostFileDescriptorReader,
    IHostFileContentReader hostFileContentReader,
    PaddleOcrIdCardClient paddleOcrIdCardClient,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<OcrIdCardTaskResponse>> CreateAsync(
        Guid actorUserId,
        CreateOcrIdCardTaskRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<OcrIdCardTaskResponse>> ConfirmAsync(
        Guid taskId,
        Guid actorUserId,
        ConfirmOcrIdCardTaskRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ConfirmCoreAsync(taskId, actorUserId, request, token),
            cancellationToken);

    public Task<Result<OcrIdCardTaskResponse>> RejectAsync(
        Guid taskId,
        Guid actorUserId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RejectCoreAsync(taskId, actorUserId, version, token),
            cancellationToken);

    private async Task<Result<OcrIdCardTaskResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateOcrIdCardTaskRequest request,
        CancellationToken cancellationToken)
    {
        var descriptor = await hostFileDescriptorReader
            .GetReadyDescriptorAsync(request.SourceFileId, cancellationToken)
            .ConfigureAwait(false);
        if (descriptor is null || !OcrSourceFileValidator.IsSupportedImage(descriptor))
        {
            return Result<OcrIdCardTaskResponse>.Failure(new Error(
                OcrErrorCodes.SourceFileInvalid,
                "The source file is missing, not ready, or is not a supported image.",
                ErrorType.Validation));
        }

        var provider = await providerQueries
            .FindRecordByKeyAsync(OcrProviderKeys.PaddleOcrIdCard, cancellationToken)
            .ConfigureAwait(false);
        if (provider is null || !provider.IsEnabled)
        {
            return Result<OcrIdCardTaskResponse>.Failure(new Error(
                OcrErrorCodes.ProviderInvalid,
                "The OCR provider is not configured or disabled.",
                ErrorType.Validation));
        }

        var taskId = idGenerator.NewId();
        var now = clock.UtcNow;
        var record = new OcrIdCardTaskRecord
        {
            Id = taskId,
            SourceFileId = request.SourceFileId,
            StatusKey = OcrIdCardTaskStatusKeys.Pending,
            CreatedAtUtc = now,
            CreatedByUserId = actorUserId,
            Version = 1,
        };
        await InsertAsync(record, cancellationToken).ConfigureAwait(false);

        var contentResult = await hostFileContentReader
            .OpenReadyContentAsync(request.SourceFileId, cancellationToken)
            .ConfigureAwait(false);
        if (!contentResult.IsSuccess || contentResult.Value is null)
        {
            return await FailAsync(record, contentResult.Error?.Message ?? "Could not open source file.", cancellationToken)
                .ConfigureAwait(false);
        }

        var fileContent = contentResult.Value;
        try
        {
            var apiKey = providerSecrets.ResolveApiKey(provider);
            var recognition = await paddleOcrIdCardClient
                .RecognizeAsync(
                    provider,
                    apiKey,
                    fileContent.Content,
                    fileContent.ContentType,
                    fileContent.OriginalFileName,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!recognition.Succeeded || recognition.Result is null)
            {
                record.RawResultJson = recognition.RawJson;
                return await FailAsync(record, recognition.Message, cancellationToken).ConfigureAwait(false);
            }

            record.StatusKey = OcrIdCardTaskStatusKeys.Recognized;
            record.RecognizedName = recognition.Result.Name;
            record.RecognizedIdNumber = recognition.Result.IdNumber;
            record.RecognizedGender = recognition.Result.Gender;
            record.RecognizedNation = recognition.Result.Nation;
            record.RecognizedAddress = recognition.Result.Address;
            record.RecognizedBirthDate = recognition.Result.BirthDate;
            record.RawResultJson = recognition.RawJson;
            record.FailureMessage = null;
            record.RecognizedAtUtc = clock.UtcNow;
            record.UpdatedAtUtc = clock.UtcNow;
            await UpdateAsync(record, cancellationToken).ConfigureAwait(false);
            return await queries.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await fileContent.Content.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Result<OcrIdCardTaskResponse>> ConfirmCoreAsync(
        Guid taskId,
        Guid actorUserId,
        ConfirmOcrIdCardTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.IdNumber))
        {
            return Result<OcrIdCardTaskResponse>.Failure(new Error(
                OcrErrorCodes.IdCardConfirmInvalid,
                "Confirmed name and ID number are required.",
                ErrorType.Validation));
        }

        var record = await queries.FindRecordAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.Version != request.Version
            || record.StatusKey != OcrIdCardTaskStatusKeys.Recognized)
        {
            return StateInvalid();
        }

        record.StatusKey = OcrIdCardTaskStatusKeys.Confirmed;
        record.ConfirmedName = request.Name.Trim();
        record.ConfirmedIdNumber = request.IdNumber.Trim();
        record.ConfirmedGender = NormalizeOptional(request.Gender);
        record.ConfirmedNation = NormalizeOptional(request.Nation);
        record.ConfirmedAddress = NormalizeOptional(request.Address);
        record.ConfirmedBirthDate = NormalizeOptional(request.BirthDate);
        record.ConfirmedAtUtc = clock.UtcNow;
        record.ConfirmedByUserId = actorUserId;
        record.UpdatedAtUtc = clock.UtcNow;
        await UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return await queries.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<OcrIdCardTaskResponse>> RejectCoreAsync(
        Guid taskId,
        Guid actorUserId,
        int version,
        CancellationToken cancellationToken)
    {
        var record = await queries.FindRecordAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.Version != version
            || record.StatusKey != OcrIdCardTaskStatusKeys.Recognized)
        {
            return StateInvalid();
        }

        record.StatusKey = OcrIdCardTaskStatusKeys.Rejected;
        record.RejectedAtUtc = clock.UtcNow;
        record.ConfirmedByUserId = actorUserId;
        record.UpdatedAtUtc = clock.UtcNow;
        await UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return await queries.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<OcrIdCardTaskResponse>> FailAsync(
        OcrIdCardTaskRecord record,
        string message,
        CancellationToken cancellationToken)
    {
        record.StatusKey = OcrIdCardTaskStatusKeys.Failed;
        record.FailureMessage = message;
        record.UpdatedAtUtc = clock.UtcNow;
        await UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return Result<OcrIdCardTaskResponse>.Failure(new Error(
            OcrErrorCodes.IdCardRecognitionFailed,
            message,
            ErrorType.Validation));
    }

    private async Task InsertAsync(OcrIdCardTaskRecord record, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                OcrIdCardTaskSql.Insert,
                OcrSqlParameters.Create(BuildParameters(record)),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task UpdateAsync(OcrIdCardTaskRecord record, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
                OcrIdCardTaskSql.Update,
                OcrSqlParameters.Create(BuildParameters(record)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            record.Version += 1;
        }
    }

    private static (string Name, object? Value)[] BuildParameters(OcrIdCardTaskRecord record) =>
    [
        ("Id", record.Id),
        ("SourceFileId", record.SourceFileId),
        ("StatusKey", record.StatusKey),
        ("RecognizedName", record.RecognizedName),
        ("RecognizedIdNumber", record.RecognizedIdNumber),
        ("RecognizedGender", record.RecognizedGender),
        ("RecognizedNation", record.RecognizedNation),
        ("RecognizedAddress", record.RecognizedAddress),
        ("RecognizedBirthDate", record.RecognizedBirthDate),
        ("ConfirmedName", record.ConfirmedName),
        ("ConfirmedIdNumber", record.ConfirmedIdNumber),
        ("ConfirmedGender", record.ConfirmedGender),
        ("ConfirmedNation", record.ConfirmedNation),
        ("ConfirmedAddress", record.ConfirmedAddress),
        ("ConfirmedBirthDate", record.ConfirmedBirthDate),
        ("RawResultJson", record.RawResultJson),
        ("FailureMessage", record.FailureMessage),
        ("RecognizedAtUtc", record.RecognizedAtUtc),
        ("ConfirmedAtUtc", record.ConfirmedAtUtc),
        ("RejectedAtUtc", record.RejectedAtUtc),
        ("ConfirmedByUserId", record.ConfirmedByUserId),
        ("CreatedAtUtc", record.CreatedAtUtc),
        ("UpdatedAtUtc", record.UpdatedAtUtc),
        ("CreatedByUserId", record.CreatedByUserId),
        ("Version", record.Version),
    ];

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<OcrIdCardTaskResponse> NotFound() =>
        Result<OcrIdCardTaskResponse>.Failure(new Error(
            OcrErrorCodes.IdCardTaskNotFound,
            "The OCR ID card task was not found.",
            ErrorType.NotFound));

    private static Result<OcrIdCardTaskResponse> StateInvalid() =>
        Result<OcrIdCardTaskResponse>.Failure(new Error(
            OcrErrorCodes.IdCardTaskStateInvalid,
            "The OCR ID card task is not in a valid state for this operation.",
            ErrorType.Validation));
}
