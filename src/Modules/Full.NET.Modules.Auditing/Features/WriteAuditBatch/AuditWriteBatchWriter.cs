using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>
/// 持久化 B1 微批（Operation/Exception/Outbound）。
/// 失败默认 fail-open；毒记录二分隔离后其余可提交；禁止降级写 Outbox。
/// </summary>
internal sealed class AuditWriteBatchWriter(
    ICommandTransaction transaction,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<AuditWriteBatchWriter> logger,
    IOptionsMonitor<AuditMicroBatchOptions> options)
{
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

        var limits = options.CurrentValue;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limits.BatchExecutionTimeout);
        var remainingAttempts = limits.MaxIsolationAttempts;
        try { await WriteAsync(envelopes).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // 等待实际可取消事务退出；不以取消等待代替取消数据库操作。
            AuditMicroBatchTelemetry.RecordFailed(cancellationToken.IsCancellationRequested ? "cancelled" : "batch_timeout");
            if (cancellationToken.IsCancellationRequested) throw;
        }
        finally
        {
            // 深层取消或隔离预算耗尽也必须终结尚未执行的右侧分支。
            CompleteAll(envelopes, succeeded: false, poisoned: false);
        }

        async Task WriteAsync(IReadOnlyList<AuditWriteEnvelope> rows)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (remainingAttempts-- <= 0)
            {
                AuditMicroBatchTelemetry.RecordRejected("isolation_budget");
                CompleteAll(rows, succeeded: false, poisoned: false);
                return;
            }
            try
            {
                await WriteMicroBatchCoreAsync(rows, deadline.Token).ConfigureAwait(false);
                CompleteAll(rows, succeeded: true, poisoned: false);
                AuditMicroBatchTelemetry.RecordFlushed(rows.Count, rows.Sum(item => item.EstimatedBytes));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (deadline.IsCancellationRequested)
            {
                // 驱动响应取消时可能返回普通数据库异常；先归一化取消，避免吞掉调用方取消或误标毒记录。
                throw new OperationCanceledException("B1 audit batch was cancelled.", exception, deadline.Token);
            }
            catch (DataCommandException exception) when (exception.Kind is
                DataCommandFailureKind.InvalidValue or DataCommandFailureKind.UniqueConstraint)
            {
                AuditMicroBatchTelemetry.RecordFailed("invalid_row");
                if (rows.Count == 1)
                {
                    AuditMicroBatchTelemetry.RecordPoisoned();
                    CompleteAll(rows, succeeded: false, poisoned: true);
                    return;
                }
                var middle = rows.Count / 2;
                await WriteAsync(rows.Take(middle).ToArray()).ConfigureAwait(false);
                await WriteAsync(rows.Skip(middle).ToArray()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 连接、死锁、超时及未知/提交结果不明的故障不能当作毒记录重复提交。
                remainingAttempts = 0;
                AuditMicroBatchTelemetry.RecordFailed("infrastructure");
                CompleteAll(rows, succeeded: false, poisoned: false);
                logger.LogWarning("B1 audit batch failed open without isolation; {Count} rows were affected.", rows.Count);
            }
        }
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
        // 每行包含事件发生时间和关联快照；预算必须与实际 SQL 参数数一致。
        var estimated =
            operationCount * 16
            + exceptionCount * 12
            + outboundCount * 14;
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
