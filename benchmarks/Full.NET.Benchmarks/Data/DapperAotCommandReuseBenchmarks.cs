using System.Data;
using BenchmarkDotNet.Attributes;
using Full.NET.Data.Dapper;
using global::Dapper;
using Microsoft.Data.SqlClient;

namespace Full.NET.Benchmarks.Data;

/// <summary>
/// 只比较命令与参数对象图的创建分配；不包含连接打开、网络或数据库执行时间。
/// </summary>
[MemoryDiagnoser]
public class DapperAotCommandReuseBenchmarks
{
    private const string Sql = "SELECT @Id, @Name, @OccurredAtUtc";
    private const string StatementName = "benchmark.dapper-aot.command-reuse";

    private readonly SqlConnection _connection = new();
    private readonly DynamicParameters _parameters = CreateParameters();
    private DapperAotCommandFactory _factory = null!;

    [GlobalSetup]
    public void WarmCommandPlan()
    {
        DapperAotStaticCommandPlanRegistry.Register(
            StatementName,
            ["Id", "Name", "OccurredAtUtc"]);
        if (!DapperAotStaticCommandPlanRegistry.TryGetFactory(
                StatementName,
                Full.NET.Data.Abstractions.DatabaseProvider.SqlServer,
                out _factory))
        {
            throw new InvalidOperationException("The benchmark command plan was not registered.");
        }

        var command = _factory.GetCommand(
            _connection,
            Sql,
            CommandType.Text,
            _parameters);
        if (!_factory.TryRecycle(command))
        {
            command.Dispose();
        }
    }

    [Benchmark(Baseline = true)]
    public int CreateBindDispose()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = Sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 30;
        foreach (var name in _parameters.ParameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = _parameters.Get<object>(name) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command.Parameters.Count;
    }

    [Benchmark]
    public int StaticPlanReuse()
    {
        if (!DapperAotStaticCommandPlanRegistry.TryGetFactory(
                StatementName,
                Full.NET.Data.Abstractions.DatabaseProvider.SqlServer,
                out var factory))
        {
            throw new InvalidOperationException("The benchmark command plan was not registered.");
        }

        var command = factory.GetCommand(
            _connection,
            Sql,
            CommandType.Text,
            _parameters);
        command.Connection = _connection;
        command.CommandTimeout = 30;
        var count = command.Parameters.Count;
        if (!factory.TryRecycle(command))
        {
            command.Dispose();
        }

        return count;
    }

    [Benchmark]
    public int DirectFactoryReuse()
    {
        var command = _factory.GetCommand(
            _connection,
            Sql,
            CommandType.Text,
            _parameters);
        command.Connection = _connection;
        var count = command.Parameters.Count;
        if (!_factory.TryRecycle(command))
        {
            command.Dispose();
        }

        return count;
    }

    [GlobalCleanup]
    public void Dispose() => _connection.Dispose();

    private static DynamicParameters CreateParameters()
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", 42);
        parameters.Add("Name", "fullnet");
        parameters.Add("OccurredAtUtc", DateTimeOffset.UnixEpoch);
        return parameters;
    }
}
