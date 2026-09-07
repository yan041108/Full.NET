using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.K3Cloud.Connectivity;
using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Domain;
using Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;
using Full.NET.Modules.K3Cloud.Persistence;
using Full.NET.Modules.K3Cloud.Security;

namespace Full.NET.Modules.K3Cloud.Features.ManageDocumentSyncs;

/// <summary>创建并重试 K3Cloud 固定单据 Save/Submit 同步；远程调用发生在意图提交之后。</summary>
/// <param name="queryExecutor">当前模块查询执行器。</param>
/// <param name="commandExecutor">当前模块写入执行器。</param>
/// <param name="transaction">本地命令事务。</param>
/// <param name="queries">同步记录查询服务。</param>
/// <param name="connectionQueries">连接配置查询服务。</param>
/// <param name="passwordProtector">连接密码保护器。</param>
/// <param name="webApiClient">金蝶 WebAPI 客户端。</param>
/// <param name="clock">业务时钟。</param>
/// <param name="idGenerator">同步记录标识生成器。</param>
internal sealed class K3CloudDocumentSyncService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    K3CloudDocumentSyncQueryService queries,
    K3CloudConnectionQueryService connectionQueries,
    K3CloudPasswordProtector passwordProtector,
    IK3CloudWebApiClient webApiClient,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>创建单据同步意图并在事务外执行 Save/Submit。</summary>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果或稳定业务错误。</returns>
    public async Task<Result<K3CloudDocumentSyncResponse>> CreateAsync(
        Guid actorUserId,
        CreateK3CloudDocumentSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await transaction.ExecuteResultAsync(
                token => PersistCreateIntentAsync(actorUserId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return Result<K3CloudDocumentSyncResponse>.Failure(prepared.Error!);
        }

        return await InvokeAndPersistAsync(prepared.Value!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>领取可重试的同步记录并再次调用金蝶。</summary>
    /// <param name="syncId">同步记录标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果或稳定业务错误。</returns>
    public async Task<Result<K3CloudDocumentSyncResponse>> RetryAsync(
        Guid syncId,
        CancellationToken cancellationToken = default)
    {
        var prepared = await transaction.ExecuteResultAsync(
                token => ClaimRetryIntentAsync(syncId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return Result<K3CloudDocumentSyncResponse>.Failure(prepared.Error!);
        }

        return await InvokeAndPersistAsync(prepared.Value!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在短事务中写入 pending 同步意图，不调用金蝶。</summary>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已提交的同步意图。</returns>
    private async Task<Result<PreparedDocumentSync>> PersistCreateIntentAsync(
        Guid actorUserId,
        CreateK3CloudDocumentSyncRequest request,
        CancellationToken cancellationToken)
    {
        var validation = K3CloudFieldValidator.ValidateDocumentSync(
            request.DocumentTypeKey,
            request.BusinessKey,
            request.PayloadJson);
        if (!validation.IsSuccess)
        {
            return Result<PreparedDocumentSync>.Failure(validation.Error!);
        }

        var connection = await connectionQueries
            .FindRecordAsync(request.ConnectionConfigId, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return ConnectionNotFound<PreparedDocumentSync>();
        }

        if (!connection.IsEnabled)
        {
            return Result<PreparedDocumentSync>.Failure(new Error(
                K3CloudErrorCodes.ConnectionInvalid,
                "The K3Cloud connection configuration is disabled.",
                ErrorType.Validation));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<K3CloudDocumentSyncRecord>(
                K3CloudDocumentSyncSql.FindByBusinessKey,
                K3CloudSqlParameters.Create([
                    ("ConnectionConfigId", request.ConnectionConfigId),
                    ("DocumentTypeKey", request.DocumentTypeKey.Trim()),
                    ("BusinessKey", request.BusinessKey.Trim())]),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<PreparedDocumentSync>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncBusinessKeyConflict,
                "A document sync record with the same business key already exists.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var record = new K3CloudDocumentSyncRecord
        {
            Id = idGenerator.NewId(),
            ConnectionConfigId = request.ConnectionConfigId,
            DocumentTypeKey = request.DocumentTypeKey.Trim(),
            BusinessKey = request.BusinessKey.Trim(),
            PayloadJson = request.PayloadJson.Trim(),
            StatusKey = K3CloudDocumentSyncStatusKeys.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = actorUserId,
            Version = 1,
        };
        await InsertAsync(record, cancellationToken).ConfigureAwait(false);
        return Result<PreparedDocumentSync>.Success(new PreparedDocumentSync(record, connection));
    }

    /// <summary>在短事务中领取可重试记录；pending 且租约未过期时拒绝并发调用。</summary>
    /// <param name="syncId">同步记录标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已领取的同步意图。</returns>
    private async Task<Result<PreparedDocumentSync>> ClaimRetryIntentAsync(
        Guid syncId,
        CancellationToken cancellationToken)
    {
        var record = await queries.FindRecordAsync(syncId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound<PreparedDocumentSync>();
        }

        if (record.StatusKey is K3CloudDocumentSyncStatusKeys.Submitted)
        {
            return Result<PreparedDocumentSync>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncRetryNotAllowed,
                "Submitted document sync records cannot be retried.",
                ErrorType.Validation));
        }

        if (record.StatusKey == K3CloudDocumentSyncStatusKeys.Pending
            && UnknownExternalSideEffect.HasActiveLease(record.UpdatedAtUtc ?? record.CreatedAtUtc, clock.UtcNow))
        {
            return Result<PreparedDocumentSync>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncInProgress,
                "A K3Cloud document sync request is already invoking the remote API.",
                ErrorType.Conflict));
        }

        var connection = await connectionQueries
            .FindRecordAsync(record.ConnectionConfigId, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return ConnectionNotFound<PreparedDocumentSync>();
        }

        var now = clock.UtcNow;
        var claimed = await commandExecutor.ExecuteAsync(
                K3CloudDocumentSyncSql.ClaimRetry,
                K3CloudSqlParameters.Create([
                    ("Id", record.Id),
                    ("UpdatedAtUtc", now),
                    ("Version", record.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (claimed == 0)
        {
            return Result<PreparedDocumentSync>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncInProgress,
                "Another retry already claimed this document sync record.",
                ErrorType.Conflict));
        }

        record.UpdatedAtUtc = now;
        record.Version += 1;
        return Result<PreparedDocumentSync>.Success(new PreparedDocumentSync(record, connection));
    }

    /// <summary>在事务外调用金蝶并回写结果；超时未知时保留已提交意图。</summary>
    /// <param name="prepared">已提交或已领取的同步意图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果。</returns>
    private async Task<Result<K3CloudDocumentSyncResponse>> InvokeAndPersistAsync(
        PreparedDocumentSync prepared,
        CancellationToken cancellationToken)
    {
        var record = prepared.Record;
        var formId = K3CloudDocumentTypeCatalog.ResolveFormId(record.DocumentTypeKey);
        if (formId is null)
        {
            return await FailAsync(
                record,
                K3CloudDocumentSyncStepKeys.Save,
                K3CloudDocumentSyncStatusKeys.SaveFailed,
                K3CloudErrorCodes.DocumentTypeUnsupported,
                "Unsupported document type.",
                cancellationToken).ConfigureAwait(false);
        }

        var password = passwordProtector.Unprotect(prepared.Connection.PasswordProtected);
        (bool Succeeded, string? BillId, string? BillNo, string Message) outcome;
        try
        {
            outcome = await webApiClient
                .SaveAndSubmitAsync(prepared.Connection, password, formId, record.PayloadJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (UnknownExternalSideEffect.Matches(exception))
        {
            return await FailAsync(
                record,
                K3CloudDocumentSyncStepKeys.Save,
                K3CloudDocumentSyncStatusKeys.ProviderUnknown,
                K3CloudErrorCodes.RemoteCallUnknown,
                exception.Message,
                cancellationToken).ConfigureAwait(false);
        }

        if (!outcome.Succeeded)
        {
            var statusKey = string.IsNullOrWhiteSpace(outcome.BillId)
                ? K3CloudDocumentSyncStatusKeys.SaveFailed
                : K3CloudDocumentSyncStatusKeys.SubmitFailed;
            var stepKey = string.IsNullOrWhiteSpace(outcome.BillId)
                ? K3CloudDocumentSyncStepKeys.Save
                : K3CloudDocumentSyncStepKeys.Submit;
            return await FailAsync(
                record,
                stepKey,
                statusKey,
                K3CloudErrorCodes.RemoteCallFailed,
                outcome.Message,
                cancellationToken,
                outcome.BillId,
                outcome.BillNo).ConfigureAwait(false);
        }

        try
        {
            record.StatusKey = K3CloudDocumentSyncStatusKeys.Submitted;
            record.LastStepKey = K3CloudDocumentSyncStepKeys.Submit;
            record.ExternalBillId = outcome.BillId;
            record.ExternalBillNo = outcome.BillNo;
            record.LastErrorCode = null;
            record.LastErrorMessage = null;
            record.SubmittedAtUtc = clock.UtcNow;
            record.UpdatedAtUtc = clock.UtcNow;
            var persisted = await transaction.ExecuteAsync(
                    token => UpdateAsync(record, token),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!persisted)
            {
                return UnknownSync("The K3Cloud document sync record could not be updated after the remote call.");
            }
        }
        catch (Exception exception)
        {
            return UnknownSync(exception.Message);
        }

        return await queries.GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>插入同步记录。</summary>
    /// <param name="record">待插入行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task InsertAsync(K3CloudDocumentSyncRecord record, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                K3CloudDocumentSyncSql.Insert,
                K3CloudSqlParameters.Create([
                    ("Id", record.Id),
                    ("ConnectionConfigId", record.ConnectionConfigId),
                    ("DocumentTypeKey", record.DocumentTypeKey),
                    ("BusinessKey", record.BusinessKey),
                    ("PayloadJson", record.PayloadJson),
                    ("StatusKey", record.StatusKey),
                    ("LastStepKey", record.LastStepKey),
                    ("ExternalBillId", record.ExternalBillId),
                    ("ExternalBillNo", record.ExternalBillNo),
                    ("LastErrorCode", record.LastErrorCode),
                    ("LastErrorMessage", record.LastErrorMessage),
                    ("SubmittedAtUtc", record.SubmittedAtUtc),
                    ("CreatedAtUtc", record.CreatedAtUtc),
                    ("UpdatedAtUtc", record.UpdatedAtUtc),
                    ("CreatedByUserId", record.CreatedByUserId),
                    ("Version", record.Version)]),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>按版本更新同步记录。</summary>
    /// <param name="record">待更新行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新成功返回 true。</returns>
    private async Task<bool> UpdateAsync(K3CloudDocumentSyncRecord record, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
                K3CloudDocumentSyncSql.Update,
                K3CloudSqlParameters.Create([
                    ("Id", record.Id),
                    ("StatusKey", record.StatusKey),
                    ("LastStepKey", record.LastStepKey),
                    ("ExternalBillId", record.ExternalBillId),
                    ("ExternalBillNo", record.ExternalBillNo),
                    ("LastErrorCode", record.LastErrorCode),
                    ("LastErrorMessage", record.LastErrorMessage),
                    ("SubmittedAtUtc", record.SubmittedAtUtc),
                    ("UpdatedAtUtc", record.UpdatedAtUtc),
                    ("Version", record.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            record.Version += 1;
            return true;
        }

        return false;
    }

    /// <summary>在独立短事务中写入失败或未知状态。</summary>
    /// <param name="record">同步记录。</param>
    /// <param name="stepKey">最近步骤。</param>
    /// <param name="statusKey">目标状态。</param>
    /// <param name="errorCode">稳定错误码。</param>
    /// <param name="message">错误摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="billId">可选外部单据标识。</param>
    /// <param name="billNo">可选外部单据编号。</param>
    /// <returns>对应业务失败结果。</returns>
    private async Task<Result<K3CloudDocumentSyncResponse>> FailAsync(
        K3CloudDocumentSyncRecord record,
        string stepKey,
        string statusKey,
        string errorCode,
        string message,
        CancellationToken cancellationToken,
        string? billId = null,
        string? billNo = null)
    {
        record.StatusKey = statusKey;
        record.LastStepKey = stepKey;
        record.ExternalBillId = billId;
        record.ExternalBillNo = billNo;
        record.LastErrorCode = errorCode;
        record.LastErrorMessage = message;
        record.UpdatedAtUtc = clock.UtcNow;
        try
        {
            await transaction.ExecuteAsync(
                    token => UpdateAsync(record, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (statusKey == K3CloudDocumentSyncStatusKeys.ProviderUnknown)
        {
            return UnknownSync(exception.Message);
        }

        return Result<K3CloudDocumentSyncResponse>.Failure(new Error(
            errorCode,
            message,
            ErrorType.Validation));
    }

    /// <summary>构造记录不存在错误。</summary>
    /// <typeparam name="T">结果值类型。</typeparam>
    /// <returns>未找到错误。</returns>
    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            K3CloudErrorCodes.DocumentSyncNotFound,
            "The K3Cloud document sync record was not found.",
            ErrorType.NotFound));

    /// <summary>构造连接不存在错误。</summary>
    /// <typeparam name="T">结果值类型。</typeparam>
    /// <returns>未找到错误。</returns>
    private static Result<T> ConnectionNotFound<T>() =>
        Result<T>.Failure(new Error(
            K3CloudErrorCodes.ConnectionNotFound,
            "The K3Cloud connection configuration was not found.",
            ErrorType.NotFound));

    /// <summary>构造远程结果未知错误。</summary>
    /// <param name="message">未知原因摘要。</param>
    /// <returns>未知状态错误。</returns>
    private static Result<K3CloudDocumentSyncResponse> UnknownSync(string message) =>
        Result<K3CloudDocumentSyncResponse>.Failure(new Error(
            K3CloudErrorCodes.RemoteCallUnknown,
            message,
            ErrorType.Conflict));

    /// <summary>已提交的单据同步意图。</summary>
    /// <param name="Record">本地同步记录。</param>
    /// <param name="Connection">远程连接配置。</param>
    private sealed record PreparedDocumentSync(
        K3CloudDocumentSyncRecord Record,
        K3CloudConnectionConfigRecord Connection);
}
