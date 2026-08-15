using System.Data.Common;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 在 Kafka 建 Topic 前验证专用数据库身份、Inbox schema 与 CDC Kafka 所有权。
/// </summary>
public sealed class KafkaCapacityDatabasePreflight(
    KafkaCapacityDatabaseConfiguration configuration)
    : IKafkaCapacityDriverPreflight
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.ConnectionString)
            || string.IsNullOrWhiteSpace(configuration.ExpectedDatabaseName)
            || configuration.CommandTimeoutSeconds <= 0
            || !Enum.IsDefined(configuration.Provider))
        {
            throw Rejected("database_configuration_invalid");
        }

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var databaseName = await ScalarAsync<string>(
                connection,
                configuration.Provider == DatabaseProvider.SqlServer
                    ? "SELECT DB_NAME();"
                    : "SELECT DATABASE();",
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    databaseName,
                    configuration.ExpectedDatabaseName,
                    StringComparison.Ordinal))
            {
                throw Rejected("database_identity_mismatch");
            }

            var schemaCount = await ScalarAsync<long>(
                connection,
                configuration.Provider == DatabaseProvider.SqlServer
                    ? "SELECT COUNT_BIG(*) FROM sys.tables WHERE name IN (N'fn_messaging_inbox_message', N'fn_messaging_stream_ownership');"
                    : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN ('fn_messaging_inbox_message', 'fn_messaging_stream_ownership');",
                cancellationToken).ConfigureAwait(false);
            if (schemaCount != 2)
            {
                throw Rejected("database_schema_missing");
            }

            await using var ownership = connection.CreateCommand();
            ownership.CommandTimeout = configuration.CommandTimeoutSeconds;
            ownership.CommandText = "SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType = @messageType AND SchemaVersion = @schemaVersion;";
            AddParameter(ownership, "@messageType", KafkaCapacityWorkerContracts.EventType);
            AddParameter(ownership, "@schemaVersion", KafkaCapacityWorkerContracts.SchemaVersion);
            var currentOwner = await ownership.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
            if (currentOwner is null
                || currentOwner is DBNull
                || Convert.ToInt32(currentOwner, System.Globalization.CultureInfo.InvariantCulture)
                    != (int)Full.NET.Messaging.Abstractions.EventDeliveryOwner.CdcKafka)
            {
                throw Rejected("database_ownership_not_cdc_kafka");
            }
        }
        catch (KafkaCapacityControlPlaneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KafkaCapacityControlPlaneException(
                "database_preflight_failed",
                $"Scope B database preflight failed without exposing connection details ({exception.GetType().Name}).");
        }
    }

    private DbConnection CreateConnection() => configuration.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(configuration.ConnectionString),
        DatabaseProvider.MySql => new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                configuration.ConnectionString,
                configuration.MySqlGuidStorageMode,
                allowUserVariables: false)),
        _ => throw Rejected("database_provider_unsupported"),
    };

    private async Task<T> ScalarAsync<T>(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = configuration.CommandTimeoutSeconds;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return (T)Convert.ChangeType(
            value ?? throw Rejected("database_preflight_empty_result"),
            typeof(T),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static KafkaCapacityControlPlaneException Rejected(string reasonCode) =>
        new(reasonCode, "Scope B database preflight rejected the target environment.");
}
