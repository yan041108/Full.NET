using System.Data.Common;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Modules.Reporting.Connectivity;

/// <summary>从受控数据源记录构造外部数据库连接；禁止接受任意连接串。</summary>
internal static class ReportingDataSourceConnectionFactory
{
    /// <summary>打开外部只读连接。</summary>
    public static async Task<(bool Succeeded, string? ErrorMessage, DbConnection? Connection)> OpenAsync(
        ReportingDataSourceRecord record,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.Equals(record.ProviderKey, ReportingDataSourceProviderKeys.SqlServer, StringComparison.Ordinal))
        {
            return await OpenSqlServerAsync(record, password, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(record.ProviderKey, ReportingDataSourceProviderKeys.MySql, StringComparison.Ordinal))
        {
            return await OpenMySqlAsync(record, password, cancellationToken).ConfigureAwait(false);
        }

        return (false, "Unsupported provider key.", null);
    }

    private static async Task<(bool Succeeded, string? ErrorMessage, DbConnection? Connection)> OpenSqlServerAsync(
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
            ConnectTimeout = ReportingExecutionPolicy.ConnectionTimeoutSeconds,
            ApplicationName = "Full.NET-Reporting-Execute",
        };

        try
        {
            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return (true, null, connection);
        }
        catch (Exception ex)
        {
            return (false, SanitizeMessage(ex.Message), null);
        }
    }

    private static async Task<(bool Succeeded, string? ErrorMessage, DbConnection? Connection)> OpenMySqlAsync(
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
            ConnectionTimeout = (uint)ReportingExecutionPolicy.ConnectionTimeoutSeconds,
            ApplicationName = "Full.NET-Reporting-Execute",
        };

        try
        {
            var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return (true, null, connection);
        }
        catch (Exception ex)
        {
            return (false, SanitizeMessage(ex.Message), null);
        }
    }

    private static string SanitizeMessage(string message) =>
        message.Length <= 512 ? message : message[..512];
}
