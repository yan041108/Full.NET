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

/// <summary>创建并重试 K3Cloud 固定单据 Save/Submit 同步。</summary>
internal sealed class K3CloudDocumentSyncService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    K3CloudDocumentSyncQueryService queries,
    K3CloudConnectionQueryService connectionQueries,
    K3CloudPasswordProtector passwordProtector,
    K3CloudWebApiClient webApiClient,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<K3CloudDocumentSyncResponse>> CreateAsync(
        Guid actorUserId,
        CreateK3CloudDocumentSyncRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<K3CloudDocumentSyncResponse>> RetryAsync(
        Guid syncId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => RetryCoreAsync(syncId, token), cancellationToken);

    private async Task<Result<K3CloudDocumentSyncResponse>> CreateCoreAsync(
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
            return Result<K3CloudDocumentSyncResponse>.Failure(validation.Error!);
        }

        var connection = await connectionQueries
            .FindRecordAsync(request.ConnectionConfigId, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return ConnectionNotFound();
        }

        if (!connection.IsEnabled)
        {
            return Result<K3CloudDocumentSyncResponse>.Failure(new Error(
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
            return Result<K3CloudDocumentSyncResponse>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncBusinessKeyConflict,
                "A document sync record with the same business key already exists.",
                ErrorType.Conflict));
        }

        var syncId = idGenerator.NewId();
        var now = clock.UtcNow;
        var record = new K3CloudDocumentSyncRecord
        {
            Id = syncId,
            ConnectionConfigId = request.ConnectionConfigId,
            DocumentTypeKey = request.DocumentTypeKey.Trim(),
            BusinessKey = request.BusinessKey.Trim(),
            PayloadJson = request.PayloadJson.Trim(),
            StatusKey = K3CloudDocumentSyncStatusKeys.Pending,
            CreatedAtUtc = now,
            CreatedByUserId = actorUserId,
            Version = 1,
        };

        await InsertAsync(record, cancellationToken).ConfigureAwait(false);
        return await ExecuteSyncAsync(record, connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<K3CloudDocumentSyncResponse>> RetryCoreAsync(
        Guid syncId,
        CancellationToken cancellationToken)
    {
        var record = await queries.FindRecordAsync(syncId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.StatusKey is K3CloudDocumentSyncStatusKeys.Submitted)
        {
            return Result<K3CloudDocumentSyncResponse>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncRetryNotAllowed,
                "Submitted document sync records cannot be retried.",
                ErrorType.Validation));
        }

        var connection = await connectionQueries
            .FindRecordAsync(record.ConnectionConfigId, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return ConnectionNotFound();
        }

        return await ExecuteSyncAsync(record, connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<K3CloudDocumentSyncResponse>> ExecuteSyncAsync(
        K3CloudDocumentSyncRecord record,
        K3CloudConnectionConfigRecord connection,
        CancellationToken cancellationToken)
    {
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

        var password = passwordProtector.Unprotect(connection.PasswordProtected);
        var outcome = await webApiClient
            .SaveAndSubmitAsync(connection, password, formId, record.PayloadJson, cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
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

        record.StatusKey = K3CloudDocumentSyncStatusKeys.Submitted;
        record.LastStepKey = K3CloudDocumentSyncStepKeys.Submit;
        record.ExternalBillId = outcome.BillId;
        record.ExternalBillNo = outcome.BillNo;
        record.LastErrorCode = null;
        record.LastErrorMessage = null;
        record.SubmittedAtUtc = now;
        record.UpdatedAtUtc = now;
        await UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return await queries.GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);
    }

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

    private async Task UpdateAsync(K3CloudDocumentSyncRecord record, CancellationToken cancellationToken)
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
        }
    }

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
        await UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return Result<K3CloudDocumentSyncResponse>.Failure(new Error(
            errorCode,
            message,
            ErrorType.Validation));
    }

    private static Result<K3CloudDocumentSyncResponse> NotFound() =>
        Result<K3CloudDocumentSyncResponse>.Failure(new Error(
            K3CloudErrorCodes.DocumentSyncNotFound,
            "The K3Cloud document sync record was not found.",
            ErrorType.NotFound));

    private static Result<K3CloudDocumentSyncResponse> ConnectionNotFound() =>
        Result<K3CloudDocumentSyncResponse>.Failure(new Error(
            K3CloudErrorCodes.ConnectionNotFound,
            "The K3Cloud connection configuration was not found.",
            ErrorType.NotFound));
}
