using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Data;

/// <summary>
/// 通过真实双库取消在途命令和多结果读取，验证统一取消边界及事务后的连接恢复。
/// </summary>
[TestClass]
public sealed class DapperCancellationTests
{
    /// <summary>
    /// 事务预先打开连接，取消仅发生在 SQL 执行或结果读取阶段。
    /// </summary>
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "execute")]
    [DataRow(DatabaseProvider.SqlServer, "single")]
    [DataRow(DatabaseProvider.SqlServer, "query")]
    [DataRow(DatabaseProvider.SqlServer, "multiple")]
    [DataRow(DatabaseProvider.SqlServer, "reader")]
    [DataRow(DatabaseProvider.MySql, "execute")]
    [DataRow(DatabaseProvider.MySql, "single")]
    [DataRow(DatabaseProvider.MySql, "query")]
    [DataRow(DatabaseProvider.MySql, "multiple")]
    [DataRow(DatabaseProvider.MySql, "reader")]
    public async Task Inflight_cancellation_is_observed_and_recovers_connection(
        DatabaseProvider provider, string operation)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var services = BuildServices(provider, connectionString);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var multiple = scope.ServiceProvider.GetRequiredService<IMultiResultQueryExecutor>();
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var delay = provider == DatabaseProvider.SqlServer
            ? "WAITFOR DELAY '00:00:10'; SELECT 2;"
            : "SELECT Value FROM fn_test_cancellation_probe WHERE SLEEP(10) = 0;";
        // MySQL 单独 SLEEP 和常量派生表都可能成功结束；读取真实临时表以触发引擎中断检查。
        // SQL Server 显式发送首结果，避免小批次缓冲到等待结束后才进入读取投影。
        var first = provider == DatabaseProvider.SqlServer
            ? "SELECT 1; RAISERROR (N'cancellation probe ready', 0, 1) WITH NOWAIT; "
            : "SELECT 1; ";
        var sql = operation == "reader" ? first + delay : delay;
        var statement = new SqlStatement("test.inflight_cancel." + operation, sql, SqlDataScope.HostOnly);
        using var cancellation = new CancellationTokenSource();
        var projectorEntered = false;

        var caught = await Assert.ThrowsAsync<OperationCanceledException>(() => transaction.ExecuteAsync(async _ =>
        {
            if (provider == DatabaseProvider.MySql)
            {
                await command.ExecuteAsync(new SqlStatement(
                    "test.inflight_cancel.setup",
                    "CREATE TEMPORARY TABLE fn_test_cancellation_probe (Value INT NOT NULL); INSERT INTO fn_test_cancellation_probe VALUES (2);",
                    SqlDataScope.HostOnly));
            }
            if (operation != "reader") cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));
            await (operation switch
            {
                "execute" => command.ExecuteAsync(statement, cancellationToken: cancellation.Token),
                "single" => query.QuerySingleOrDefaultAsync<int>(statement, cancellationToken: cancellation.Token),
                "query" => (Task)query.QueryAsync<int>(statement, cancellationToken: cancellation.Token),
                "multiple" or "reader" => multiple.QueryMultipleAsync(statement, null, async (reader, _) =>
                {
                    projectorEntered = true;
                    if (operation == "reader") cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));
                    await reader.ReadSingleOrDefaultAsync<int>();
                    if (operation == "reader") await reader.ReadSingleOrDefaultAsync<int>();
                    return 0;
                }, cancellation.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            });
            return 0;
        }, CancellationToken.None));

        // 驱动已有的取消异常可携带内部令牌，执行层保持原异常；映射后的令牌身份由单测验证。
        Assert.IsTrue(cancellation.IsCancellationRequested);
        if (operation == "reader") Assert.IsTrue(projectorEntered, "取消必须覆盖已创建的多结果读取器。");
        Assert.AreEqual(9, await query.QuerySingleOrDefaultAsync<int>(
            new SqlStatement("test.after_inflight_cancel", "SELECT 9;", SqlDataScope.HostOnly)));
    }

    private static ServiceProvider BuildServices(DatabaseProvider provider, string connectionString)
    {
        // 私有用例将 Provider 池与应用准入都限为一条，后续查询可发现遗留租约。
        connectionString = provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(connectionString) { MaxPoolSize = 1 }.ConnectionString
            : new MySqlConnectionStringBuilder(connectionString) { MaximumPoolSize = 1 }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{DatabaseCapacityOptions.SectionName}:Enabled"] = "true",
            [$"{DatabaseCapacityOptions.SectionName}:HostRole"] = "Api",
            [$"{DatabaseCapacityOptions.SectionName}:PermitLimit"] = "1",
            [$"{DatabaseCapacityOptions.SectionName}:QueueLimit"] = "0",
            [$"{DatabaseCapacityOptions.SectionName}:AcquireTimeoutMilliseconds"] = "1000",
            [$"{DatabaseCapacityOptions.SectionName}:ExpectedMaxPoolSize"] = "1",
            [$"{DatabaseCapacityOptions.SectionName}:ApiMaxReplicas"] = "1",
            [$"{DatabaseCapacityOptions.SectionName}:ApiMaxPoolSize"] = "1",
            [$"{DatabaseCapacityOptions.SectionName}:WorkerMaxReplicas"] = "1",
            [$"{DatabaseCapacityOptions.SectionName}:WorkerMaxPoolSize"] = "1",
            [$"{DatabaseCapacityOptions.SectionName}:TotalBudget"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddFullNetDapper(configuration, "Testing");
        // Outbox 依赖由专用测试覆盖；本组仅激活真实数据执行和事务边界。
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });
    }
}
