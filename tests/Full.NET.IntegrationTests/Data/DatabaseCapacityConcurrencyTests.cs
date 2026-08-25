using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
[DoNotParallelize]
public sealed class DatabaseCapacityConcurrencyTests
{
    [TestMethod]
    public async Task SqlServer_TinyPoolBoundsQueueCancellationAndRecovery()
    {
        var connectionString = new SqlConnectionStringBuilder(
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync())
        {
            MaxPoolSize = 2,
            MinPoolSize = 0,
            ConnectRetryCount = 0,
        }.ConnectionString;

        await AssertTinyPoolBehaviorAsync(
            DatabaseProvider.SqlServer,
            connectionString,
            "WAITFOR DELAY '00:00:02'; SELECT 1");
    }

    [TestMethod]
    public async Task MySql_TinyPoolBoundsQueueCancellationAndRecovery()
    {
        var connectionString = new MySqlConnectionStringBuilder(
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync())
        {
            MaximumPoolSize = 2,
            MinimumPoolSize = 0,
        }.ConnectionString;

        await AssertTinyPoolBehaviorAsync(
            DatabaseProvider.MySql,
            connectionString,
            "SELECT SLEEP(2)");
    }

    private static async Task AssertTinyPoolBehaviorAsync(
        DatabaseProvider provider,
        string connectionString,
        string slowSql)
    {
        await using var services = CreateServices(provider, connectionString);
        var slowQuery = QueryAsync(
            services,
            new SqlStatement(
                "capacity.slow_probe",
                slowSql,
                SqlDataScope.Global));
        await Task.Delay(300);

        using var queuedCancellation = new CancellationTokenSource();
        var queued = QueryAsync(
            services,
            new SqlStatement(
                "capacity.queued_probe",
                "SELECT 1",
                SqlDataScope.Global),
            queuedCancellation.Token);
        await Task.Delay(100);

        var rejected = await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(
            () => QueryAsync(
                services,
                new SqlStatement(
                    "capacity.rejected_probe",
                    "SELECT 1",
                    SqlDataScope.Global)));
        Assert.AreEqual(ServiceCapacityFailureKind.Rejected, rejected.Kind);

        queuedCancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => queued);
        await slowQuery;

        var recovered = await QueryAsync(
            services,
            new SqlStatement(
                "capacity.recovery_probe",
            "SELECT 1",
                SqlDataScope.Global));
        Assert.AreEqual(1, recovered);

        await using var retainedScope = services.CreateAsyncScope();
        var retainedExecutor = retainedScope.ServiceProvider
            .GetRequiredService<IQueryExecutor>();
        var first = await retainedExecutor.QuerySingleOrDefaultAsync<int>(
            new SqlStatement(
                "capacity.command_lease_first_probe",
                "SELECT 1",
                SqlDataScope.Global));
        Assert.AreEqual(1, first);

        // 第一个 DI Scope 仍存活；第二个 Scope 能执行证明许可证在命令结束时已经归还。
        var competing = await QueryAsync(
            services,
            new SqlStatement(
                "capacity.command_lease_competing_probe",
                "SELECT 1",
                SqlDataScope.Global));
        Assert.AreEqual(1, competing);
    }

    private static ServiceProvider CreateServices(
        DatabaseProvider provider,
        string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = provider.ToString(),
                ["Database:ConnectionString"] = connectionString,
                ["Database:MySqlGuidStorageMode"] = "Binary16",
                ["DatabaseCapacity:Enabled"] = "true",
                ["DatabaseCapacity:HostRole"] = "Api",
                ["DatabaseCapacity:PermitLimit"] = "1",
                ["DatabaseCapacity:QueueLimit"] = "1",
                ["DatabaseCapacity:AcquireTimeoutMilliseconds"] = "1000",
                ["DatabaseCapacity:ExpectedMaxPoolSize"] = "2",
                ["DatabaseCapacity:HealthReserve"] = "1",
                ["DatabaseCapacity:CriticalWorkerReserve"] = "0",
                ["DatabaseCapacity:ApiMaxReplicas"] = "1",
                ["DatabaseCapacity:ApiMaxPoolSize"] = "2",
                ["DatabaseCapacity:WorkerMaxReplicas"] = "1",
                ["DatabaseCapacity:WorkerMaxPoolSize"] = "1",
                ["DatabaseCapacity:MigrationReserve"] = "0",
                ["DatabaseCapacity:TotalBudget"] = "3",
            })
            .Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddScoped<CurrentTenantAccessor>();
        serviceCollection.AddScoped<ICurrentTenant>(serviceProvider =>
            serviceProvider.GetRequiredService<CurrentTenantAccessor>());
        serviceCollection.AddFullNetDapper(
            configuration,
            Environments.Development);
        return serviceCollection.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
            });
    }

    private static async Task<int?> QueryAsync(
        ServiceProvider services,
        SqlStatement statement,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<int>(
                statement,
                cancellationToken: cancellationToken);
    }
}
