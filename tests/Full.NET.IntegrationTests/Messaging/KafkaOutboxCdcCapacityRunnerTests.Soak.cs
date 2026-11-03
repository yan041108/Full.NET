using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using Dapper;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Messaging;

public sealed partial class KafkaOutboxCdcCapacityRunnerTests
{
    private static async Task InterruptOwnedSoakBrokerAsync(CdcDebeziumPipelineEnvironment pipeline, string directory, CancellationToken token,
        Func<CancellationToken, Task> sampleBacklog)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(30), token);
            var started = DateTimeOffset.UtcNow;
            await pipeline.InterruptBrokerAsync(TimeSpan.FromSeconds(10), token, sampleBacklog);
            await File.WriteAllTextAsync(Path.Combine(directory, "broker-pause.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                StartedAtUtc = started, RecoveredAtUtc = DateTimeOffset.UtcNow,
                Scope = "Owned single Broker pause; not Kafka N+1",
            }), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    /// <summary>显式长跑使用独享数据库与管线，不使用既有共享夹具或未知环境连接。</summary>
    internal static async Task<ScopeCScenario> CreateOwnedSoakScenarioAsync(DatabaseProvider provider)
    {
        const string databaseName = "fullnet_messaging_soak";
        IAsyncDisposable databaseOwner;
        string connectionString;
        string databaseId;
        if (provider == DatabaseProvider.SqlServer)
        {
            var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword(SharedDatabaseFixture.MySqlRootPassword).WithEnvironment("MSSQL_AGENT_ENABLED", "true")
                .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048").WithCreateParameterModifier(p =>
                {
                    var limits = p.HostConfig ??= new(); limits.Memory = 3L << 30;
                    limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
                }).Build();
            databaseOwner = server;
            try
            {
                await server.StartAsync();
                var settings = new SqlConnectionStringBuilder(server.GetConnectionString());
                await using (var admin = new SqlConnection(settings.ConnectionString)) await admin.ExecuteAsync("CREATE DATABASE " + databaseName);
                settings.InitialCatalog = databaseName; connectionString = settings.ConnectionString; databaseId = server.Id;
            }
            catch { await databaseOwner.DisposeAsync(); throw; }
        }
        else
        {
            var server = new MySqlBuilder("mysql:8.0").WithDatabase(databaseName).WithUsername("root")
                .WithPassword(SharedDatabaseFixture.MySqlRootPassword)
                .WithCommand("--log-bin-trust-function-creators=1", "--log-bin=mysql-bin", "--binlog-format=ROW", "--binlog-row-image=FULL", "--server-id=1840172611")
                .WithCreateParameterModifier(p =>
                {
                    var limits = p.HostConfig ??= new(); limits.Memory = 1536L << 20;
                    limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
                }).Build();
            databaseOwner = server;
            try { await server.StartAsync(); connectionString = server.GetConnectionString(); databaseId = server.Id; }
            catch { await databaseOwner.DisposeAsync(); throw; }
        }
        CdcDebeziumPipelineEnvironment? pipeline = null;
        try
        {
            await MigrateAndSeedOwnershipAsync(provider, connectionString);
            if (provider == DatabaseProvider.SqlServer)
            {
                var enabled = await SqlServerCdcTestSupport.TryEnableCdcAsync(connectionString);
                Assert.IsTrue(enabled.Succeeded, enabled.FailureReason);
            }
            pipeline = await CdcDebeziumPipelineEnvironment.StartAsync();
            return new(pipeline, pipeline.CreateConnectAdminClient(), new DatabaseOptions
            {
                Provider = provider, ConnectionString = connectionString, CommandTimeoutSeconds = 60,
                MySqlGuidStorageMode = provider == DatabaseProvider.MySql ? MySqlGuidStorageMode.Binary16 : MySqlGuidStorageMode.LegacyChar36,
            }, connectionString, databaseName, databaseOwner, [databaseId, .. pipeline.OwnedContainerIds]);
        }
        catch
        {
            if (pipeline is not null) await pipeline.DisposeAsync();
            await databaseOwner.DisposeAsync(); throw;
        }
    }
}
