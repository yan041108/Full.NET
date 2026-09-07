using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Connectivity;

/// <summary>把报表数据源记录映射为 Full.NET 数据边界的外部连接请求。</summary>
internal static class ReportingExternalConnectionMapper
{
    /// <summary>将受控数据源映射为外部连接请求；未知提供程序返回空。</summary>
    /// <param name="record">已持久化的数据源行。</param>
    /// <param name="password">解保护后的明文密码。</param>
    /// <param name="applicationName">驱动应用程序名。</param>
    /// <returns>可打开的请求；提供程序不受支持时为空。</returns>
    public static ExternalDatabaseConnectionRequest? TryCreate(
        ReportingDataSourceRecord record,
        string password,
        string applicationName)
    {
        if (string.Equals(record.ProviderKey, ReportingDataSourceProviderKeys.SqlServer, StringComparison.Ordinal))
        {
            return Create(DatabaseProvider.SqlServer, record, password, applicationName);
        }

        if (string.Equals(record.ProviderKey, ReportingDataSourceProviderKeys.MySql, StringComparison.Ordinal))
        {
            return Create(DatabaseProvider.MySql, record, password, applicationName);
        }

        return null;
    }

    /// <summary>按提供程序填充外部连接参数，超时与执行策略保持一致。</summary>
    /// <param name="provider">目标提供程序。</param>
    /// <param name="record">数据源行。</param>
    /// <param name="password">明文密码。</param>
    /// <param name="applicationName">驱动应用程序名。</param>
    private static ExternalDatabaseConnectionRequest Create(
        DatabaseProvider provider,
        ReportingDataSourceRecord record,
        string password,
        string applicationName) =>
        new(
            provider,
            record.ServerHost,
            record.Port,
            record.DatabaseName,
            record.Username,
            password,
            record.TrustServerCertificate,
            ReportingExecutionPolicy.ConnectionTimeoutSeconds,
            applicationName);
}
