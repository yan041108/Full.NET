using Dapper;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// SQL Server CDC 集成测试辅助：检测 Agent、启用 CDC，并支持外部实例连接串。
/// </summary>
internal static class SqlServerCdcTestSupport
{
    internal const string ExternalConnectionEnvironmentVariable =
        "FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING";

    internal sealed record CdcEnablementResult(
        bool Succeeded,
        string? FailureReason,
        bool AgentUnavailable,
        bool UsedExternalInstance);

    internal static bool TryGetExternalConnectionString(out string connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable(
            ExternalConnectionEnvironmentVariable) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    internal static async Task<string> ResolveConnectionStringAsync()
    {
        if (TryGetExternalConnectionString(out var external))
        {
            return external;
        }

        return await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
    }

    internal static async Task<CdcEnablementResult> TryEnableCdcAsync(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var usedExternal = TryGetExternalConnectionString(out _);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        if (!await IsSqlServerAgentRunningAsync(connection))
        {
            if (!usedExternal)
            {
                return new CdcEnablementResult(
                    false,
                    "SQL Server Agent is not running; CDC capture jobs cannot start.",
                    AgentUnavailable: true,
                    usedExternal);
            }
        }

        try
        {
            await connection.ExecuteAsync("EXEC sys.sp_cdc_enable_db;");
            await connection.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM cdc.change_tables
                    WHERE capture_instance = N'fullnet_fn_messaging_outbox_event')
                BEGIN
                    EXEC sys.sp_cdc_enable_table
                        @source_schema = N'dbo',
                        @source_name = N'fn_messaging_outbox_event',
                        @role_name = NULL,
                        @capture_instance = N'fullnet_fn_messaging_outbox_event',
                        @supports_net_changes = 0;
                END
                """);
            return new CdcEnablementResult(true, null, false, usedExternal);
        }
        catch (Exception exception)
        {
            return new CdcEnablementResult(
                false,
                exception.Message,
                AgentUnavailable: false,
                usedExternal);
        }
    }

    internal static string BuildInconclusiveMessage(CdcEnablementResult result)
    {
        if (result.AgentUnavailable && !result.UsedExternalInstance)
        {
            return "SQL Server CDC could not be enabled in the Testcontainers instance "
                + "(SQL Server Agent/capture job gap). "
                + "See docs/verification/sqlserver-cdc-ci-debt.md.";
        }

        return "SQL Server CDC could not be enabled: "
            + (result.FailureReason ?? "unknown error")
            + ". See docs/verification/sqlserver-cdc-ci-debt.md.";
    }

    private static async Task<bool> IsSqlServerAgentRunningAsync(SqlConnection connection)
    {
        try
        {
            var status = await connection.QuerySingleOrDefaultAsync<string?>(
                """
                SELECT TOP (1) status_desc
                FROM sys.dm_server_services
                WHERE servicename LIKE N'SQL Server Agent (%'
                """);
            return string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
