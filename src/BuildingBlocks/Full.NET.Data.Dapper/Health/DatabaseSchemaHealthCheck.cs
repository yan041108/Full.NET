using System.Data.Common;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Health;

internal sealed class DatabaseSchemaHealthCheck(
    DbConnectionFactory connectionFactory,
    IOptions<DatabaseOptions> databaseOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(
                databaseOptions.Value.CommandTimeoutSeconds,
                1,
                5)));

            await using var connection = connectionFactory.Create();
            await connection.OpenAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = GetSchemaContractQuery(databaseOptions.Value.Provider);
            command.CommandTimeout = Math.Clamp(
                databaseOptions.Value.CommandTimeoutSeconds,
                1,
                5);
            var schemaMode = Convert.ToString(
                await command.ExecuteScalarAsync(timeout.Token),
                System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(schemaMode)
                ? HealthCheckResult.Unhealthy("数据库 Schema Contract 尚未就绪。")
                : HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("数据库 Schema Contract 健康检查超时。");
        }
        catch (DbException)
        {
            return HealthCheckResult.Unhealthy("数据库 Schema Contract 健康检查失败。");
        }
        catch (InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("数据库 Schema Contract 健康检查失败。");
        }
    }

    private static string GetSchemaContractQuery(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer =>
            """
            SELECT COALESCE(
                (SELECT TOP (1) SchemaMode
                 FROM dbo.fn_uuid_contract_state
                 WHERE Id = 1),
                '')
            """,
        DatabaseProvider.MySql =>
            """
            SELECT COALESCE(
                (SELECT SchemaMode
                 FROM fn_uuid_contract_state
                 WHERE Id = 1
                 LIMIT 1),
                '')
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };
}
