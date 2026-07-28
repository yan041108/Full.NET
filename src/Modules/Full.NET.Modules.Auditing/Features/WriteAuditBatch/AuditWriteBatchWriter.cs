using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>
/// 将请求内固定三槽 Audit 快照作为单条数据库命令原子提交，避免逐类串行往返。
/// </summary>
internal sealed class AuditWriteBatchWriter(
    ICommandTransaction transaction,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<AuditWriteBatchWriter> logger)
{
    public async Task<bool> TryWriteAsync(
        AuditWriteBuffer buffer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var batch = buffer.Snapshot();
        if (batch.Count == 0)
        {
            return true;
        }

        try
        {
            var statement = AuditWriteBatchSql.Get(batch.Kinds);
            var parameters = CreateParameters(batch);
            var affectedRows = await transaction.ExecuteAsync(
                    token => commandExecutor.ExecuteAsync(
                        statement,
                        parameters,
                        token),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows != batch.Count)
            {
                throw new InvalidOperationException(
                    $"Audit batch affected {affectedRows} rows instead of {batch.Count}.");
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to persist request audit batch {AuditKinds}.",
                batch.Kinds);
            return false;
        }
    }

    private object CreateParameters(AuditWriteBatch batch)
    {
        var occurredAtUtc = clock.UtcNow;
        return new
        {
            OccurredAtUtc = occurredAtUtc,
            AccessId = batch.Access is null ? (Guid?)null : idGenerator.NewId(),
            AccessHttpMethod = batch.Access?.HttpMethod,
            AccessRequestPath = batch.Access?.RequestPath,
            AccessStatusCode = batch.Access?.StatusCode,
            AccessDurationMs = batch.Access?.DurationMs,
            AccessUserId = batch.Access?.UserId,
            AccessTenantId = batch.Access?.TenantId,
            AccessTraceId = batch.Access?.TraceId,
            AccessClientIpFingerprint = batch.Access?.ClientIpFingerprint,
            AccessIsAuthenticated = batch.Access?.IsAuthenticated,
            OperationId = batch.Operation is null ? (Guid?)null : idGenerator.NewId(),
            OperationActionKey = batch.Operation?.ActionKey,
            OperationHttpMethod = batch.Operation?.HttpMethod,
            OperationRequestPath = batch.Operation?.RequestPath,
            OperationStatusCode = batch.Operation?.StatusCode,
            OperationDurationMs = batch.Operation?.DurationMs,
            OperationSucceeded = batch.Operation?.Succeeded,
            OperationUserId = batch.Operation?.UserId,
            OperationTenantId = batch.Operation?.TenantId,
            OperationTraceId = batch.Operation?.TraceId,
            OperationClientIpFingerprint = batch.Operation?.ClientIpFingerprint,
            OperationPermissionCode = batch.Operation?.PermissionCode,
            ExceptionId = batch.Exception is null ? (Guid?)null : idGenerator.NewId(),
            ExceptionType = batch.Exception?.ExceptionType,
            ExceptionMessage = batch.Exception?.Message,
            ExceptionStackTrace = batch.Exception?.StackTrace,
            ExceptionHttpMethod = batch.Exception?.HttpMethod,
            ExceptionRequestPath = batch.Exception?.RequestPath,
            ExceptionUserId = batch.Exception?.UserId,
            ExceptionTenantId = batch.Exception?.TenantId,
            ExceptionTraceId = batch.Exception?.TraceId,
            ExceptionClientIpFingerprint = batch.Exception?.ClientIpFingerprint,
        };
    }
}
