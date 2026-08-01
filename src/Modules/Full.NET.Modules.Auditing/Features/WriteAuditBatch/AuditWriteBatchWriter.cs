using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>
/// 持久化 Access（过渡期同步）与 B1 微批（Operation/Exception/Outbound）。
/// 微批失败默认 fail-open；毒记录二分隔离后其余可提交；禁止降级写 Outbox。
/// </summary>
internal sealed class AuditWriteBatchWriter(
    ICommandTransaction transaction,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<AuditWriteBatchWriter> logger)
{
    public async Task<bool> TryWriteAccessAsync(
        AccessLogWriteModel access,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        try
        {
            var (statement, parameters) = AuditWriteBatchSql.BuildAccess(
                access,
                idGenerator.NewId(),
                clock.UtcNow);
            var affected = await transaction.ExecuteAsync(
                    token => commandExecutor.ExecuteAsync(statement, parameters, token),
                    cancellationToken)
                .ConfigureAwait(false);
            return affected == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to persist transitional access audit.");
            return false;
        }
    }

    /// <summary>
    /// 将一批 B1 信封写入审计库；同批各表共享一次事务。失败时二分隔离毒记录。
    /// </summary>
    public async Task WriteMicroBatchAsync(
        IReadOnlyList<AuditWriteEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        if (envelopes.Count == 0)
        {
            return;
        }

        try
        {
            await WriteMicroBatchCoreAsync(envelopes, cancellationToken).ConfigureAwait(false);
            CompleteAll(envelopes, succeeded: true, poisoned: false);
            AuditMicroBatchTelemetry.RecordFlushed(
                envelopes.Count,
                envelopes.Sum(item => item.EstimatedBytes));
        }
        catch (OperationCanceledException)
        {
            // 停机或超时取消时必须解除请求等待，保持 B1 fail-open。
            CompleteAll(envelopes, succeeded: false, poisoned: false);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "B1 micro-batch of {Count} audit rows failed; isolating poison records.",
                envelopes.Count);
            AuditMicroBatchTelemetry.RecordFailed("batch");
            await IsolatePoisonAsync(envelopes, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task IsolatePoisonAsync(
        IReadOnlyList<AuditWriteEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        if (envelopes.Count == 1)
        {
            // 单条仍失败：标记为毒记录并 fail-open，不写 Outbox。
            AuditMicroBatchTelemetry.RecordPoisoned();
            CompleteAll(envelopes, succeeded: false, poisoned: true);
            logger.LogWarning(
                "Poisoned B1 audit row of kind {Kind} was dropped after isolation.",
                envelopes[0].Kind);
            return;
        }

        var mid = envelopes.Count / 2;
        var left = envelopes.Take(mid).ToArray();
        var right = envelopes.Skip(mid).ToArray();
        await WriteMicroBatchAsync(left, cancellationToken).ConfigureAwait(false);
        await WriteMicroBatchAsync(right, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMicroBatchCoreAsync(
        IReadOnlyList<AuditWriteEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var occurredAtUtc = clock.UtcNow;
        var operations = new List<(Guid Id, OperationLogWriteModel Model)>();
        var exceptions = new List<(Guid Id, ExceptionLogWriteModel Model)>();
        var outbounds = new List<OutboundCallLogRecord>();

        foreach (var envelope in envelopes)
        {
            switch (envelope.Kind)
            {
                case AuditMicroBatchKind.Operation:
                    operations.Add((idGenerator.NewId(), envelope.Operation!));
                    break;
                case AuditMicroBatchKind.Exception:
                    exceptions.Add((idGenerator.NewId(), envelope.Exception!));
                    break;
                case AuditMicroBatchKind.Outbound:
                    outbounds.Add(envelope.Outbound!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(envelopes));
            }
        }

        EnsureParameterBudget(operations.Count, exceptions.Count, outbounds.Count);

        var operationSql = AuditWriteBatchSql.BuildOperations(operations, occurredAtUtc);
        var exceptionSql = AuditWriteBatchSql.BuildExceptions(exceptions, occurredAtUtc);
        var outboundSql = AuditWriteBatchSql.BuildOutbounds(outbounds);

        await transaction.ExecuteAsync(
                async token =>
                {
                    var total = 0;
                    if (operationSql.Statement is not null)
                    {
                        total += await commandExecutor.ExecuteAsync(
                                operationSql.Statement,
                                operationSql.Parameters,
                                token)
                            .ConfigureAwait(false);
                    }

                    if (exceptionSql.Statement is not null)
                    {
                        total += await commandExecutor.ExecuteAsync(
                                exceptionSql.Statement,
                                exceptionSql.Parameters,
                                token)
                            .ConfigureAwait(false);
                    }

                    if (outboundSql.Statement is not null)
                    {
                        total += await commandExecutor.ExecuteAsync(
                                outboundSql.Statement,
                                outboundSql.Parameters,
                                token)
                            .ConfigureAwait(false);
                    }

                    if (total != envelopes.Count)
                    {
                        throw new InvalidOperationException(
                            $"B1 micro-batch affected {total} rows instead of {envelopes.Count}.");
                    }

                    return total;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureParameterBudget(
        int operationCount,
        int exceptionCount,
        int outboundCount)
    {
        // Operation≈12、Exception≈10、Outbound≈13，外加共享 OccurredAtUtc。
        var estimated =
            (operationCount == 0 ? 0 : 1 + operationCount * 12)
            + (exceptionCount == 0 ? 0 : 1 + exceptionCount * 10)
            + outboundCount * 13;
        if (estimated > AuditWriteBatchSql.MaxSqlParameters)
        {
            throw new InvalidOperationException(
                $"B1 micro-batch estimated {estimated} SQL parameters, exceeding "
                + $"{AuditWriteBatchSql.MaxSqlParameters}; shrink MaxBatchRows.");
        }
    }

    private static void CompleteAll(
        IReadOnlyList<AuditWriteEnvelope> envelopes,
        bool succeeded,
        bool poisoned)
    {
        var result = new AuditWriteResult(succeeded, poisoned);
        foreach (var envelope in envelopes)
        {
            envelope.Completion.TrySetResult(result);
        }
    }
}
