using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// MySQL CDC → Debezium E2E 共享场景；环境不足时由调用方 Inconclusive。
/// </summary>
internal sealed class CdcDebeziumMySqlE2EScenario : IAsyncDisposable
{
    private CdcDebeziumMySqlE2EScenario(
        CdcDebeziumPipelineEnvironment pipeline,
        KafkaConnectAdminClient connectAdmin,
        DatabaseOptions options,
        string connectionString)
    {
        Pipeline = pipeline;
        ConnectAdmin = connectAdmin;
        Options = options;
        ConnectionString = connectionString;
    }

    public CdcDebeziumPipelineEnvironment Pipeline { get; }

    public KafkaConnectAdminClient ConnectAdmin { get; }

    public DatabaseOptions Options { get; }

    public string ConnectionString { get; }

    public static async Task<CdcDebeziumMySqlE2EScenario?> TryCreateAsync()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var binlogStatus = await CdcShadowFixture.ReadMySqlBinlogStatusAsync(connectionString);
        if (!binlogStatus.IsRowFullEnabled)
        {
            return null;
        }

        var pipeline = await CdcDebeziumPipelineFixture.GetOrStartAsync();
        var connectAdmin = pipeline.CreateConnectAdminClient();
        if (!await connectAdmin.WaitUntilReadyAsync(TimeSpan.FromSeconds(60)))
        {
            return null;
        }

        return new CdcDebeziumMySqlE2EScenario(
            pipeline,
            connectAdmin,
            options,
            connectionString);
    }

    public Task<IReadOnlyDictionary<string, string>> CreateConnectorConfigAsync() =>
        DebeziumConnectorTemplateFactory.CreateMySqlShadowConfigAsync(
            ConnectionString,
            Pipeline.HostGateway,
            Pipeline.InternalKafkaBootstrapServers);

    public async ValueTask DisposeAsync()
    {
        ConnectAdmin.Dispose();
        await ValueTask.CompletedTask;
    }
}
