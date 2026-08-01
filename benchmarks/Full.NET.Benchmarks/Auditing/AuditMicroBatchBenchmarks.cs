using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Full.NET.Benchmarks.Auditing;

/// <summary>
/// B1 微批相对回归基准：对比逐条命令与跨请求多行插入的命令数与耗时。
/// 不宣称容量达标（Capacity-not-verified）。
/// </summary>
[MemoryDiagnoser]
public class AuditMicroBatchBenchmarks
{
    private AuditWriteBatchWriter _writer = null!;
    private CountingExecutor _executor = null!;
    private long _commands;
    private long _transactions;
    private double _flushMs;

    [GlobalSetup]
    public void Setup()
    {
        _executor = new CountingExecutor();
        _writer = new AuditWriteBatchWriter(
            new CountingTransaction(() => Interlocked.Increment(ref _transactions)),
            _executor,
            new FixedClock(),
            new SequenceIdGenerator(),
            NullLogger<AuditWriteBatchWriter>.Instance);
    }

    [Benchmark(Baseline = true)]
    public async Task Write_one_row_per_command()
    {
        Reset();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < 64; i++)
        {
            await _writer.WriteMicroBatchAsync(
                [AuditWriteEnvelope.ForOperation(CreateOperation($"single-{i}"))],
                CancellationToken.None);
        }

        _flushMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _commands = _executor.Commands;
        _ = _commands;
        _ = _transactions;
        _ = _flushMs;
    }

    [Benchmark]
    public async Task Write_sixty_four_rows_one_batch()
    {
        Reset();
        var envelopes = Enumerable.Range(0, 64)
            .Select(i => AuditWriteEnvelope.ForOperation(CreateOperation($"batch-{i}")))
            .ToArray();
        var started = Stopwatch.GetTimestamp();
        await _writer.WriteMicroBatchAsync(envelopes, CancellationToken.None);
        _flushMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _commands = _executor.Commands;
        _ = _commands;
        _ = _transactions;
        _ = _flushMs;
    }

    private void Reset()
    {
        _commands = 0;
        _transactions = 0;
        _flushMs = 0;
        _executor.Reset();
    }

    private static OperationLogWriteModel CreateOperation(string actionKey) =>
        new(
            actionKey,
            "POST",
            "/api/v1/benchmark/audit",
            200,
            1,
            true,
            null,
            null,
            "bench-trace",
            null,
            "bench.permission");

    private sealed class CountingTransaction(Action onExecute) : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            onExecute();
            return action(cancellationToken);
        }
    }

    private sealed class CountingExecutor : ICommandExecutor
    {
        public long Commands { get; private set; }

        public void Reset() => Commands = 0;

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Commands++;
            var dict = parameters as Dictionary<string, object?> ?? [];
            var rows = dict.Keys.Count(key => key.EndsWith("_Id", StringComparison.Ordinal));
            return Task.FromResult(Math.Max(1, rows));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }
}
