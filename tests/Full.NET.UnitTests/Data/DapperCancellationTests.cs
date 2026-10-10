using System.Data;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Reflection;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

/// <summary>
/// 验证 Provider 取消候选在统一执行边界的传播、诊断和故障保留。
/// </summary>
[TestClass]
public sealed class DapperCancellationTests
{
    private static readonly string[] Operations = ["typed", "execute", "single", "query", "multiple"];

    /// <summary>
    /// 五个入口均保留原始异常与调用令牌，并将执行指标记为取消。
    /// </summary>
    [TestMethod]
    [DataRow("typed", DatabaseProvider.SqlServer)]
    [DataRow("execute", DatabaseProvider.SqlServer)]
    [DataRow("single", DatabaseProvider.SqlServer)]
    [DataRow("query", DatabaseProvider.SqlServer)]
    [DataRow("multiple", DatabaseProvider.SqlServer)]
    [DataRow("typed", DatabaseProvider.MySql)]
    [DataRow("execute", DatabaseProvider.MySql)]
    [DataRow("single", DatabaseProvider.MySql)]
    [DataRow("query", DatabaseProvider.MySql)]
    [DataRow("multiple", DatabaseProvider.MySql)]
    public async Task Canceled_provider_candidate_preserves_context_and_records_canceled(
        string operation, DatabaseProvider provider)
    {
        using var cancellation = new CancellationTokenSource();
        var original = CreateProviderException(provider, provider == DatabaseProvider.SqlServer ? 0 : 1317);
        var statementName = "test.provider_cancel." + Guid.NewGuid().ToString("N");
        var measurements = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == DapperTelemetry.MeterName
                    && instrument.Name == "fullnet.data.sql.executions")
                {
                    current.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (tags.ToArray().Any(tag => tag.Key == "statement_name" && Equals(tag.Value, statementName)))
            {
                measurements.Add(tags.ToArray());
            }
        });
        listener.Start();

        var caught = await CaptureAsync(operation, provider, original, cancellation, true, statementName);

        Assert.IsInstanceOfType<OperationCanceledException>(caught);
        var canceled = (OperationCanceledException)caught;
        Assert.AreEqual(cancellation.Token, canceled.CancellationToken);
        Assert.AreSame(original, canceled.InnerException);
        Assert.AreEqual(1, measurements.Count);
        Assert.AreEqual("canceled", measurements[0].Single(tag => tag.Key == "outcome").Value);
        Assert.AreEqual("canceled", measurements[0].Single(tag => tag.Key == "failure_reason").Value);
    }

    /// <summary>
    /// 没有调用者取消时，Provider 候选仍是数据库故障。
    /// </summary>
    [TestMethod]
    [DataRow("typed", DatabaseProvider.SqlServer)]
    [DataRow("execute", DatabaseProvider.SqlServer)]
    [DataRow("single", DatabaseProvider.SqlServer)]
    [DataRow("query", DatabaseProvider.SqlServer)]
    [DataRow("multiple", DatabaseProvider.SqlServer)]
    [DataRow("typed", DatabaseProvider.MySql)]
    [DataRow("execute", DatabaseProvider.MySql)]
    [DataRow("single", DatabaseProvider.MySql)]
    [DataRow("query", DatabaseProvider.MySql)]
    [DataRow("multiple", DatabaseProvider.MySql)]
    public async Task Uncanceled_provider_candidate_remains_original(string operation, DatabaseProvider provider)
    {
        using var cancellation = new CancellationTokenSource();
        var original = CreateProviderException(provider, provider == DatabaseProvider.SqlServer ? 0 : 1317);
        Assert.AreSame(original, await CaptureAsync(operation, provider, original, cancellation, false));
    }

    /// <summary>
    /// 取消与真实故障竞态时，查询保留原故障；写入仍优先保持唯一键和死锁分类。
    /// </summary>
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, -2)]
    [DataRow(DatabaseProvider.SqlServer, 102)]
    [DataRow(DatabaseProvider.SqlServer, 229)]
    [DataRow(DatabaseProvider.SqlServer, 18456)]
    [DataRow(DatabaseProvider.SqlServer, 2601)]
    [DataRow(DatabaseProvider.SqlServer, 1205)]
    [DataRow(DatabaseProvider.MySql, 1205)]
    [DataRow(DatabaseProvider.MySql, 1064)]
    [DataRow(DatabaseProvider.MySql, 1142)]
    [DataRow(DatabaseProvider.MySql, 1045)]
    [DataRow(DatabaseProvider.MySql, 1062)]
    [DataRow(DatabaseProvider.MySql, 1213)]
    public async Task Cancellation_does_not_mask_database_failures(DatabaseProvider provider, int number)
    {
        var original = CreateProviderException(provider, number);
        foreach (var operation in Operations)
        {
            using var cancellation = new CancellationTokenSource();
            var caught = await CaptureAsync(operation, provider, original, cancellation, true);
            if ((operation is "execute" or "typed") && DataCommandExceptionMapper.TryMap(original, out var expected))
            {
                Assert.IsInstanceOfType<DataCommandException>(caught);
                Assert.AreEqual(expected.Kind, ((DataCommandException)caught).Kind);
                Assert.AreSame(original, caught.InnerException);
            }
            else
            {
                Assert.AreSame(original, caught);
            }
        }
    }

    /// <summary>
    /// SQL Server 一组错误包含明确故障码时，不能仅凭首个零码吞掉故障。
    /// </summary>
    [TestMethod]
    public async Task Mixed_sqlserver_errors_remain_original()
    {
        var original = CreateSqlException(0, 102);
        foreach (var operation in Operations)
        {
            using var cancellation = new CancellationTokenSource();
            Assert.AreSame(original, await CaptureAsync(operation, DatabaseProvider.SqlServer, original, cancellation, true));
        }
    }

    /// <summary>
    /// 非数据库异常和已经规范化的取消异常不被替换。
    /// </summary>
    [TestMethod]
    public async Task Application_and_existing_cancellation_exceptions_remain_original()
    {
        foreach (var original in new Exception[] { new InvalidOperationException("fixture"), new OperationCanceledException() })
        {
            foreach (var operation in Operations)
            {
                using var cancellation = new CancellationTokenSource();
                Assert.AreSame(original, await CaptureAsync(operation, DatabaseProvider.SqlServer, original, cancellation, true));
            }
        }
    }

    private static async Task<Exception> CaptureAsync(
        string operation, DatabaseProvider provider, Exception original,
        CancellationTokenSource cancellation, bool cancel, string statementName = "test.provider_cancel")
    {
        // 入场时令牌未取消；在 Provider 打开阶段触发竞态，确保每个执行器 catch 都实际运行。
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);
        connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (cancel) cancellation.Cancel();
            return Task.FromException(original);
        });
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.Create().Returns(connection);
        var options = Options.Create(new DatabaseOptions { Provider = provider });
        var capacity = Options.Create(new DatabaseCapacityOptions
        {
            Enabled = true, HostRole = DatabaseHostRole.Api, PermitLimit = 1,
            QueueLimit = 0, AcquireTimeoutMilliseconds = 100,
        });
        using var telemetry = new DatabaseConnectionTelemetry(options, capacity);
        using var gate = new DatabaseAdmissionGate(capacity, telemetry);
        await using var session = new DbSession(factory, gate, telemetry, new DatabaseAdmissionPriorityScope());
        var tenant = new CurrentTenantAccessor();
        tenant.SetHost();
        var executor = new DapperSqlExecutor(session, tenant, options, NullLogger<DapperSqlExecutor>.Instance);
        var statement = new SqlStatement(statementName, "SELECT 1;", SqlDataScope.HostOnly);
        try
        {
            await (operation switch
            {
                "typed" => executor.ExecuteTypedAsync(statement, "fixture", new TestPlan(), cancellation.Token),
                "execute" => executor.ExecuteAsync(statement, cancellationToken: cancellation.Token),
                "single" => executor.QuerySingleOrDefaultAsync<int>(statement, cancellationToken: cancellation.Token),
                "query" => (Task)executor.QueryAsync<int>(statement, cancellationToken: cancellation.Token),
                "multiple" => executor.QueryMultipleAsync(statement, null, (_, _) => Task.FromResult(0), cancellation.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            });
        }
        catch (Exception caught)
        {
            Assert.AreEqual(0, gate.InUseCount, "失败或取消后必须释放连接预算。");
            return caught;
        }

        Assert.Fail("执行器应传播 fixture 异常。");
        throw new InvalidOperationException();
    }

    private static Exception CreateProviderException(DatabaseProvider provider, int number) =>
        provider == DatabaseProvider.SqlServer ? CreateSqlException(number) :
            (MySqlException)typeof(MySqlException).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(MySqlErrorCode), typeof(string)], null)!
                .Invoke([(MySqlErrorCode)number, "fixture"]);

    private static SqlException CreateSqlException(params int[] numbers)
    {
        // 仅测试通过 Provider 内部构造器建立真实异常；生产取消映射保持静态闭包。
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var constructor = typeof(SqlError).GetConstructor(flags, null,
            [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)], null)!;
        var add = typeof(SqlErrorCollection).GetMethod("Add", flags)!;
        foreach (var number in numbers)
        {
            var error = constructor.Invoke([number, (byte)0, (byte)11, "fixture", "fixture", "fixture", 1, null]);
            add.Invoke(errors, [error]);
        }

        return (SqlException)typeof(SqlException).GetConstructor(flags, null,
            [typeof(string), typeof(SqlErrorCollection), typeof(Exception), typeof(Guid)], null)!
            .Invoke(["fixture", errors, null, Guid.Empty]);
    }

    private sealed class TestPlan() : DapperTypedCommandPlan<string>("SELECT 1;")
    {
        protected override void AddParameters(DbCommand command) { }
        protected override void UpdateParameters(DbCommand command, string args) { }
    }
}
