using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

internal sealed class MySqlSchemaModeStartupValidator(
    IOptions<DatabaseOptions> databaseOptions) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = databaseOptions.Value;
        if (options.Provider != DatabaseProvider.MySql)
        {
            return;
        }

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                options.ConnectionString,
                options.MySqlGuidStorageMode,
                allowUserVariables: false));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_uuid_contract_state'
            """;
        var stateTableExists = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
        var schemaMode = string.Empty;
        if (stateTableExists)
        {
            command.CommandText =
                "SELECT COALESCE(SchemaMode, '') FROM fn_uuid_contract_state WHERE Id = 1";
            schemaMode = Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        var expectsBinary = options.MySqlGuidStorageMode == MySqlGuidStorageMode.Binary16;
        var isBinary = string.Equals(schemaMode, "Binary16", StringComparison.Ordinal);
        if (expectsBinary != isBinary)
        {
            // 必须在 API/Worker 接收流量前阻止应用模式与不可兼容 schema 交叉连接。
            throw new InvalidOperationException(
                "MySQL UUID 应用模式与数据库 Contract schema 状态不一致。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
