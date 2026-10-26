using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 从 deploy/messaging 冻结模板生成测试用 Connector 配置。
/// </summary>
internal static class DebeziumConnectorTemplateFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // 受控采集证明使用版本化 Topic 目录；旧传输测试的 EventType 路由不能充当生产证明地址。
    private static string ControlledShadowTopic => CdcShadowFixture.ShadowTopicPrefix + "."
        + IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged.TopicCode;

    public static async Task<IReadOnlyDictionary<string, string>> CreateSqlServerShadowConfigAsync(
        string connectionString,
        string hostGateway,
        string kafkaBootstrapServers,
        CancellationToken cancellationToken = default,
        bool useFormalOutbox = false)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var template = await LoadTemplateAsync(
                useFormalOutbox ? "deploy/messaging/connectors/sqlserver-outbox-capacity.json" : "deploy/messaging/connectors/sqlserver-outbox-shadow.json",
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
            ["FULLNET_SHADOW_TOPIC"] = ControlledShadowTopic,
        };
        var resolved = new Dictionary<string, string>(
            Substitute(template.Config, replacements),
            StringComparer.Ordinal);
        // Connect 容器经 hostGateway 访问宿主机 SQL Server；JDBC 与 .NET 连接串 TLS 策略分离。
        resolved["database.encrypt"] = "false";
        resolved["database.trustServerCertificate"] = "true";
        // 正式追加表的旧传输场景显式使用正式源模板，仍投递测试影子 Topic，不能混入受控采集证明。
        if (useFormalOutbox)
        {
            resolved["transforms.outbox.route.topic.replacement"] = CdcShadowFixture.ShadowTopicPrefix + ".${routedByValue}";
            resolved["schema.history.internal.kafka.topic"] = "fullnet.dev.shadow.internal.schema-history.sqlserver";
        }
        return resolved;
    }

    public static async Task<IReadOnlyDictionary<string, string>> CreateMySqlShadowConfigAsync(
        string connectionString,
        string hostGateway,
        string kafkaBootstrapServers,
        CancellationToken cancellationToken = default,
        bool useFormalOutbox = false)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var rootBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            UserID = "root",
            Password = SharedDatabaseFixture.MySqlRootPassword,
            Database = builder.Database,
        };
        var template = await LoadTemplateAsync(
                useFormalOutbox ? "deploy/messaging/connectors/mysql-outbox-capacity.json" : "deploy/messaging/connectors/mysql-outbox-shadow.json",
                cancellationToken)
            .ConfigureAwait(false);
        var port = builder.Port > 0 ? builder.Port : 3306;
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FULLNET_MYSQL_HOST"] = hostGateway,
            ["FULLNET_MYSQL_PORT"] = port.ToString(),
            ["FULLNET_MYSQL_USER"] = rootBuilder.UserID,
            ["FULLNET_MYSQL_PASSWORD"] = rootBuilder.Password,
            ["FULLNET_MYSQL_DATABASE"] = builder.Database,
            ["FULLNET_KAFKA_BOOTSTRAP_SERVERS"] = kafkaBootstrapServers,
            ["FULLNET_SHADOW_TOPIC"] = ControlledShadowTopic,
        };
        var resolved = new Dictionary<string, string>(Substitute(template.Config, replacements), StringComparer.Ordinal);
        if (useFormalOutbox)
        {
            resolved["transforms.outbox.route.topic.replacement"] = CdcShadowFixture.ShadowTopicPrefix + ".${routedByValue}";
            resolved["schema.history.internal.kafka.topic"] = "fullnet.dev.shadow.internal.schema-history.mysql";
        }
        return resolved;
    }

    private static async Task<ConnectorTemplateDocument> LoadTemplateAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = FullNetApiFactory.FindRepositoryRoot();
        var absolutePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        await using var stream = File.OpenRead(absolutePath);
        var document = await JsonSerializer.DeserializeAsync<ConnectorTemplateDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Connector template is invalid: {relativePath}");
        return document;
    }

    private static IReadOnlyDictionary<string, string> Substitute(
        IReadOnlyDictionary<string, string> config,
        IReadOnlyDictionary<string, string> replacements)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in config)
        {
            var substituted = value;
            foreach (var (token, replacement) in replacements)
            {
                substituted = substituted.Replace(
                    "${" + token + "}",
                    replacement,
                    StringComparison.Ordinal);
            }

            // 业务路由表达式由 Connect 解析；部署变量必须已由工厂解析，错误不能带着凭据回显配置。
            if (substituted.Contains("${FULLNET_", StringComparison.Ordinal))
                throw new InvalidOperationException("Connector template contains an unresolved deployment placeholder.");
            resolved[key] = substituted;
        }

        return resolved;
    }

    private sealed record ConnectorTemplateDocument(
        string Name,
        Dictionary<string, string> Config);
}
