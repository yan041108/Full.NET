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

/// <summary>创建、识别、确认与拒绝身份证 OCR 任务；识别请求发生在任务意图提交之后。</summary>
/// <param name="commandExecutor">当前模块写入执行器。</param>
/// <param name="transaction">本地命令事务。</param>
/// <param name="queries">任务查询服务。</param>
/// <param name="providerQueries">Provider 配置查询服务。</param>
/// <param name="providerSecrets">Provider 密钥解析器。</param>
    /// <param name="hostFileDescriptorReader">Host 文件描述读取端口；必须在事务外调用。</param>
/// <param name="hostFileContentReader">Host 文件内容读取端口。</param>
/// <param name="paddleOcrIdCardClient">身份证识别客户端。</param>
/// <param name="clock">业务时钟。</param>
/// <param name="idGenerator">任务标识生成器。</param>
internal sealed class OcrIdCardTaskService(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OcrIdCardTaskQueryService queries,
    OcrProviderQueryService providerQueries,
    OcrProviderSecretResolver providerSecrets,
    IHostFileDescriptorReader hostFileDescriptorReader,
    IHostFileContentReader hostFileContentReader,
    IPaddleOcrIdCardClient paddleOcrIdCardClient,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>读取源文件描述后提交 pending 任务意图，再在事务外调用 OCR。</summary>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任务结果或稳定业务错误。</returns>
    public async Task<Result<OcrIdCardTaskResponse>> CreateAsync(
        Guid actorUserId,
        CreateOcrIdCardTaskRequest request,
        CancellationToken cancellationToken = default)
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

        var prepared = await transaction.ExecuteResultAsync(
                token => PersistTaskIntentAsync(actorUserId, request, descriptor, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return Result<OcrIdCardTaskResponse>.Failure(prepared.Error!);
        }

        return await RecognizeOutsideTransactionAsync(prepared.Value!, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>确认已识别的身份证结果；该路径不调用外部提供程序。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="request">确认请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的任务。</returns>
    public Task<Result<OcrIdCardTaskResponse>> ConfirmAsync(
        Guid taskId,
        Guid actorUserId,
        ConfirmOcrIdCardTaskRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ConfirmCoreAsync(taskId, actorUserId, request, token),
            cancellationToken);

    /// <summary>拒绝已识别的身份证结果；该路径不调用外部提供程序。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="version">任务乐观版本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的任务。</returns>
    public Task<Result<OcrIdCardTaskResponse>> RejectAsync(
        Guid taskId,
        Guid actorUserId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RejectCoreAsync(taskId, actorUserId, version, token),
            cancellationToken);

    /// <summary>在短事务中写入 pending 任务意图，并校验 Provider 可用性。</summary>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="descriptor">事务外已校验的源文件描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已提交的任务意图。</returns>
    private async Task<Result<PreparedOcrTask>> PersistTaskIntentAsync(
        Guid actorUserId,
        CreateOcrIdCardTaskRequest request,
        HostFileDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var provider = await providerQueries
            .FindRecordByKeyAsync(OcrProviderKeys.PaddleOcrIdCard, cancellationToken)
            .ConfigureAwait(false);
        if (provider is null || !provider.IsEnabled)
        {
            return Result<PreparedOcrTask>.Failure(new Error(
                OcrErrorCodes.ProviderInvalid,
                "The OCR provider is not configured or disabled.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var record = new OcrIdCardTaskRecord
        {
            Id = idGenerator.NewId(),
            SourceFileId = request.SourceFileId,
            StatusKey = OcrIdCardTaskStatusKeys.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = actorUserId,
            Version = 1,
        };
        await InsertAsync(record, cancellationToken).ConfigureAwait(false);
        return Result<PreparedOcrTask>.Success(new PreparedOcrTask(record, provider, descriptor));
    }

    /// <summary>打开源文件并调用 OCR；超时未知时保留 pending/unknown 意图。</summary>
    /// <param name="prepared">已提交的任务意图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>识别结果。</returns>
    private async Task<Result<OcrIdCardTaskResponse>> RecognizeOutsideTransactionAsync(
        PreparedOcrTask prepared,
        CancellationToken cancellationToken)
    {
        var record = prepared.Record;
        var contentResult = await hostFileContentReader
            .OpenReadyContentAsync(prepared.Descriptor.FileId, cancellationToken)
            .ConfigureAwait(false);
        if (!contentResult.IsSuccess || contentResult.Value is null)
        {
            return await FailAsync(
                    record,
                    OcrIdCardTaskStatusKeys.Failed,
                    OcrErrorCodes.IdCardRecognitionFailed,
                    contentResult.Error?.Message ?? "Could not open source file.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var fileContent = contentResult.Value;
        try
        {
            var apiKey = providerSecrets.ResolveApiKey(prepared.Provider);
            var recognition = await paddleOcrIdCardClient
                .RecognizeAsync(
                    prepared.Provider,
                    apiKey,
                    fileContent.Content,
                    fileContent.ContentType,
                    fileContent.OriginalFileName,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!recognition.Succeeded || recognition.Result is null)
            {
                record.RawResultJson = recognition.RawJson;
                return await FailAsync(
                        record,
                        OcrIdCardTaskStatusKeys.Failed,
                        OcrErrorCodes.IdCardRecognitionFailed,
                        recognition.Message,
                        cancellationToken)
                    .ConfigureAwait(false);
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
            try
            {
                var persisted = await transaction.ExecuteAsync(
                        token => UpdateAsync(record, token),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!persisted)
                {
                    return UnknownTask("The OCR ID card task could not be updated after recognition.");
                }
            }
            catch (Exception exception)
            {
                return UnknownTask(exception.Message);
            }

            return await queries.GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (UnknownExternalSideEffect.Matches(exception))
        {
            return await FailAsync(
                    record,
                    OcrIdCardTaskStatusKeys.ProviderUnknown,
                    OcrErrorCodes.RemoteCallUnknown,
                    exception.Message,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await fileContent.Content.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>确认识别结果。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="request">确认请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的任务。</returns>
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

    /// <summary>拒绝识别结果。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="version">任务乐观版本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的任务。</returns>
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

    /// <summary>在独立短事务中写入失败或未知状态。</summary>
    /// <param name="record">任务记录。</param>
    /// <param name="statusKey">目标状态。</param>
    /// <param name="errorCode">稳定错误码。</param>
    /// <param name="message">错误摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对应业务失败结果。</returns>
    private async Task<Result<OcrIdCardTaskResponse>> FailAsync(
        OcrIdCardTaskRecord record,
        string statusKey,
        string errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        record.StatusKey = statusKey;
        record.FailureMessage = message;
        record.UpdatedAtUtc = clock.UtcNow;
        try
        {
            await transaction.ExecuteAsync(
                    token => UpdateAsync(record, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (statusKey == OcrIdCardTaskStatusKeys.ProviderUnknown)
        {
            return UnknownTask(exception.Message);
        }

        return Result<OcrIdCardTaskResponse>.Failure(new Error(
            errorCode,
            message,
            ErrorType.Validation));
    }

    /// <summary>插入任务记录。</summary>
    /// <param name="record">待插入行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task InsertAsync(OcrIdCardTaskRecord record, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                OcrIdCardTaskSql.Insert,
                OcrSqlParameters.Create(BuildParameters(record)),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>按版本更新任务记录。</summary>
    /// <param name="record">待更新行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新成功返回 true。</returns>
    private async Task<bool> UpdateAsync(OcrIdCardTaskRecord record, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
                OcrIdCardTaskSql.Update,
                OcrSqlParameters.Create(BuildParameters(record)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            record.Version += 1;
            return true;
        }

        return false;
    }

    /// <summary>构造任务持久化参数。</summary>
    /// <param name="record">任务记录。</param>
    /// <returns>命名参数集合。</returns>
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

    /// <summary>规范化可选文本字段。</summary>
    /// <param name="value">原始文本。</param>
    /// <returns>空白时返回 null。</returns>
    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    /// <summary>构造任务不存在错误。</summary>
    /// <returns>未找到错误。</returns>
    private static Result<OcrIdCardTaskResponse> NotFound() =>
        Result<OcrIdCardTaskResponse>.Failure(new Error(
            OcrErrorCodes.IdCardTaskNotFound,
            "The OCR ID card task was not found.",
            ErrorType.NotFound));

    /// <summary>构造任务状态不允许当前操作的错误。</summary>
    /// <returns>状态错误。</returns>
    private static Result<OcrIdCardTaskResponse> StateInvalid() =>
        Result<OcrIdCardTaskResponse>.Failure(new Error(
            OcrErrorCodes.IdCardTaskStateInvalid,
            "The OCR ID card task is not in a valid state for this operation.",
            ErrorType.Validation));

    /// <summary>构造识别结果未知错误。</summary>
    /// <param name="message">未知原因摘要。</param>
    /// <returns>未知状态错误。</returns>
    private static Result<OcrIdCardTaskResponse> UnknownTask(string message) =>
        Result<OcrIdCardTaskResponse>.Failure(new Error(
            OcrErrorCodes.RemoteCallUnknown,
            message,
            ErrorType.Conflict));

    /// <summary>已提交的身份证 OCR 任务意图。</summary>
    /// <param name="Record">本地任务记录。</param>
    /// <param name="Provider">识别提供程序配置。</param>
    /// <param name="Descriptor">已校验的源文件描述。</param>
    private sealed record PreparedOcrTask(
        OcrIdCardTaskRecord Record,
        OcrProviderConfigRecord Provider,
        HostFileDescriptor Descriptor);
}
