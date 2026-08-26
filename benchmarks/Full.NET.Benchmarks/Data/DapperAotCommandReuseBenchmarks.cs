using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using global::Dapper;
using Microsoft.Data.SqlClient;

namespace Full.NET.Benchmarks.Data;

/// <summary>
/// 比较 Outbox insert 命令对象图在四条路径上的时间与分配；不包含连接打开、网络或数据库执行。
/// </summary>
[MemoryDiagnoser]
public class DapperAotCommandReuseBenchmarks
{
    // 批量执行 160_000 次，使四条路径的单次 iteration 都超过 BenchmarkDotNet 建议的 100 ms。
    private const int OperationsPerBatch = 160_000;

    private readonly SqlConnection _connection = new();
    private SqlTransaction? _transaction;
    private OutboxMessage _message = OutboxInsertCommandBenchmarkHarness.CreateSampleMessage();
    private DynamicParameters _parameters = null!;
    private DapperAotCommandFactory _fixedFactory = null!;
    private OutboxInsertTypedCommandPrototype _typedPrototype = null!;

    [GlobalSetup]
    public void WarmCommandPlan()
    {
        OutboxInsertCommandBenchmarkHarness.RegisterPlan();
        if (!DapperAotStaticCommandPlanRegistry.TryGetFactory(
                OutboxInsertCommandBenchmarkHarness.StatementName,
                DatabaseProvider.SqlServer,
                out _fixedFactory))
        {
            throw new InvalidOperationException("The benchmark command plan was not registered.");
        }

        _message = OutboxInsertCommandBenchmarkHarness.CreateSampleMessage();
        _parameters = OutboxInsertCommandBenchmarkHarness.BindDynamicParameters(_message);
        _typedPrototype = new OutboxInsertTypedCommandPrototype(
            OutboxInsertCommandBenchmarkHarness.Sql);

        try
        {
            _connection.Open();
            _transaction = _connection.BeginTransaction();
        }
        catch
        {
            _transaction = null;
        }

        AssertTypedCommandReuse();
        WarmPath(() => CreateBindDispose());
        WarmPath(() => StaticRegistryPlan());
        WarmPath(() => FixedFactoryHandle());
        WarmPath(() => TypedParameterFactoryPrototype());
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerBatch)]
    public int CreateBindDispose()
    {
        var total = 0;
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            total += OutboxInsertCommandBenchmarkHarness.CreateBindDispose(
                _connection,
                _transaction,
                _parameters);
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerBatch)]
    public int StaticRegistryPlan()
    {
        var total = 0;
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            if (!DapperAotStaticCommandPlanRegistry.TryGetFactory(
                    OutboxInsertCommandBenchmarkHarness.StatementName,
                    DatabaseProvider.SqlServer,
                    out var factory))
            {
                throw new InvalidOperationException("The benchmark command plan was not registered.");
            }

            var command = factory.GetCommand(
                _connection,
                OutboxInsertCommandBenchmarkHarness.Sql,
                CommandType.Text,
                _parameters);
            total += OutboxInsertCommandBenchmarkHarness.FinalizeCommand(
                command,
                _connection,
                _transaction,
                factory);
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerBatch)]
    public int FixedFactoryHandle()
    {
        var total = 0;
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            var command = _fixedFactory.GetCommand(
                _connection,
                OutboxInsertCommandBenchmarkHarness.Sql,
                CommandType.Text,
                _parameters);
            total += OutboxInsertCommandBenchmarkHarness.FinalizeCommand(
                command,
                _connection,
                _transaction,
                _fixedFactory);
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerBatch)]
    public int TypedParameterFactoryPrototype()
    {
        var total = 0;
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            var command = _typedPrototype.GetCommand(
                _connection,
                _message,
                CommandType.Text);
            total += OutboxInsertCommandBenchmarkHarness.FinalizeTypedCommand(
                command,
                _connection,
                _transaction,
                _typedPrototype);
        }

        return total;
    }

    [IterationSetup]
    public void RefreshMessage()
    {
        _message = OutboxInsertCommandBenchmarkHarness.CreateSampleMessage();
        _parameters = OutboxInsertCommandBenchmarkHarness.BindDynamicParameters(_message);
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _transaction?.Dispose();
        _connection.Dispose();
    }

    private void AssertTypedCommandReuse()
    {
        var first = _typedPrototype.GetCommand(_connection, _message, CommandType.Text);
        OutboxInsertCommandBenchmarkHarness.FinalizeTypedCommand(
            first,
            _connection,
            _transaction,
            _typedPrototype);
        var second = _typedPrototype.GetCommand(_connection, _message, CommandType.Text);
        if (!ReferenceEquals(first, second))
        {
            throw new InvalidOperationException(
                "Typed command reuse did not return the same command instance.");
        }

        if (first.Connection is not null || first.Transaction is not null)
        {
            throw new InvalidOperationException(
                "Recycled typed command must detach connection and transaction.");
        }

        OutboxInsertCommandBenchmarkHarness.FinalizeTypedCommand(
            second,
            _connection,
            _transaction,
            _typedPrototype);
    }

    private static void WarmPath(Func<int> path)
    {
        for (var index = 0; index < 4; index++)
        {
            _ = path();
        }
    }
}
