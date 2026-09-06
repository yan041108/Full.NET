using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Modules.Reporting.Connectivity;

/// <summary>在受控白名单提供程序上执行只读连接测试；禁止接受任意连接串。</summary>
internal static class ReportingDataSourceConnectionTester
{
    private const int ConnectionTimeoutSeconds = 10;

    /// <summary>测试数据源连接并执行 <c>SELECT 1</c> 探活。</summary>
    /// <param name="record">已持久化的数据源行。</param>
    /// <param name="password">解保护后的明文密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果消息。</returns>
    public static async Task<(bool Succeeded, string Message)> TestAsync(
        ReportingDataSourceRecord record,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(record.ProviderKey, ReportingDataSourceProviderKeys.SqlServer, StringComparison.Ordinal))
        {
            return await TestSqlServerAsync(record, password, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(record.ProviderKey, ReportingDataSourceProviderKeys.MySql, StringComparison.Ordinal))
        {
            return await TestMySqlAsync(record, password, cancellationToken).ConfigureAwait(false);
        }

        return (false, "Unsupported provider key.");
    }

    private static async Task<(bool Succeeded, string Message)> TestSqlServerAsync(
        ReportingDataSourceRecord record,
        string password,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{record.ServerHost},{record.Port}",
            InitialCatalog = record.DatabaseName,
            UserID = record.Username,
            Password = password,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = record.TrustServerCertificate,
            ConnectTimeout = ConnectionTimeoutSeconds,
            ApplicationName = "Full.NET-Reporting-Test",
        };

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = ConnectionTimeoutSeconds;
            var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is null)
            {
                return (false, "Connection opened but probe query returned no result.");
            }

            return (true, "Connected successfully. Ensure the account is read-only.");
        }
        catch (Exception ex)
        {
            return (false, SanitizeMessage(ex.Message));
        }
    }

    private static async Task<(bool Succeeded, string Message)> TestMySqlAsync(
        ReportingDataSourceRecord record,
        string password,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = record.ServerHost,
            Port = (uint)record.Port,
            Database = record.DatabaseName,
            UserID = record.Username,
            Password = password,
            ConnectionTimeout = (uint)ConnectionTimeoutSeconds,
            ApplicationName = "Full.NET-Reporting-Test",
        };

        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = ConnectionTimeoutSeconds;
            var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is null)
            {
                return (false, "Connection opened but probe query returned no result.");
            }

            return (true, "Connected successfully. Ensure the account is read-only.");
        }
        catch (Exception ex)
        {
            return (false, SanitizeMessage(ex.Message));
        }
    }

    private static string SanitizeMessage(string message)
    {
        if (message.Length <= 512)
        {
            return message;
        }

        return message[..512];
    }
}
