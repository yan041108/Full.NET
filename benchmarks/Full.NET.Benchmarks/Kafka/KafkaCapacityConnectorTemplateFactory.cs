using System.Text.Json;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 从 deploy/messaging 冻结模板生成容量 Connector 配置。
/// </summary>
public static class KafkaCapacityConnectorTemplateFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyDictionary<string, string>> CreateConfigAsync(
        DatabaseProvider provider,
        string connectionString,
        KafkaCapacityConnectConfiguration connect,
        string kafkaBootstrapServers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(kafkaBootstrapServers);
        return provider switch
        {
            DatabaseProvider.SqlServer => await CreateSqlServerConfigAsync(
                connectionString,
                connect.DatabaseHostGateway,
                kafkaBootstrapServers,
                cancellationToken).ConfigureAwait(false),
            DatabaseProvider.MySql => await CreateMySqlConfigAsync(
                connectionString,
                connect,
                kafkaBootstrapServers,
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidDataException("database_provider_unsupported"),
        };
    }

    public static string ResolveCdcTopicName(string messageType) =>
        $"fullnet.capacity.cdc.{messageType}";

    public static string BuildConnectorName(string prefix, string runId) =>
        $"{prefix}-{KafkaCapacityFingerprint.Sha256(runId)[..12]}";

    private static async Task<IReadOnlyDictionary<string, string>> CreateSqlServerConfigAsync(
        string connectionString,
        string hostGateway,
        string kafkaBootstrapServers,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var template = await LoadTemplateAsync(
                "deploy/messaging/connectors/sqlserver-outbox-capacity.json",
                cancellationToken)
            .ConfigureAwait(false);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FULLNET_SQLSERVER_HOST"] = hostGateway,
            ["FULLNET_SQLSERVER_PORT"] = builder.DataSource.Contains(',')
                ? builder.DataSource.Split(',', 2)[1]
                : "1433",
            ["FULLNET_SQLSERVER_USER"] = builder.UserID,
            ["FULLNET_SQLSERVER_PASSWORD"] = builder.Password,
            ["FULLNET_SQLSERVER_DATABASE"] = builder.InitialCatalog,
            ["FULLNET_KAFKA_BOOTSTRAP_SERVERS"] = kafkaBootstrapServers,
        };
        return Substitute(template.Config, replacements);
    }

    private static async Task<IReadOnlyDictionary<string, string>> CreateMySqlConfigAsync(
        string connectionString,
        KafkaCapacityConnectConfiguration connect,
        string kafkaBootstrapServers,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var template = await LoadTemplateAsync(
                "deploy/messaging/connectors/mysql-outbox-capacity.json",
                cancellationToken)
            .ConfigureAwait(false);
        var port = builder.Port > 0 ? builder.Port : 3306;
        var connectorUser = string.IsNullOrWhiteSpace(connect.MySqlConnectorUser)
            ? builder.UserID
            : connect.MySqlConnectorUser;
        var connectorPassword = connect.MySqlConnectorPassword ?? builder.Password;
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FULLNET_MYSQL_HOST"] = connect.DatabaseHostGateway,
            ["FULLNET_MYSQL_PORT"] = port.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["FULLNET_MYSQL_USER"] = connectorUser,
            ["FULLNET_MYSQL_PASSWORD"] = connectorPassword,
            ["FULLNET_MYSQL_DATABASE"] = builder.Database,
            ["FULLNET_KAFKA_BOOTSTRAP_SERVERS"] = kafkaBootstrapServers,
        };
        return Substitute(template.Config, replacements);
    }

    private static async Task<ConnectorTemplateDocument> LoadTemplateAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var absolutePath = Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        await using var stream = File.OpenRead(absolutePath);
        return await JsonSerializer.DeserializeAsync<ConnectorTemplateDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Connector template is invalid.");
    }

    private static IReadOnlyDictionary<string, string> Substitute(
        IReadOnlyDictionary<string, string> template,
        IReadOnlyDictionary<string, string> replacements)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in template)
        {
            var resolvedValue = value;
            foreach (var (placeholder, replacement) in replacements)
            {
                resolvedValue = resolvedValue.Replace(
                    "${" + placeholder + "}",
                    replacement,
                    StringComparison.Ordinal);
            }

            resolved[key] = resolvedValue;
        }

        return resolved;
    }

    private sealed class ConnectorTemplateDocument
    {
        public Dictionary<string, string> Config { get; init; } = [];
    }
}
