using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>报表数据源字段校验与提供程序默认端口。</summary>
internal static class ReportingDataSourceFieldValidator
{
    /// <summary>名称允许的最大字符数。</summary>
    internal const int MaxNameLength = 128;

    /// <summary>主机名允许的最大字符数。</summary>
    internal const int MaxHostLength = 256;

    /// <summary>数据库名与用户名允许的最大字符数。</summary>
    internal const int MaxIdentifierLength = 128;

    /// <summary>SQL Server 默认端口。</summary>
    internal const int DefaultSqlServerPort = 1433;

    /// <summary>MySQL 默认端口。</summary>
    internal const int DefaultMySqlPort = 3306;

    /// <summary>校验提供程序键是否在受支持白名单内。</summary>
    /// <param name="providerKey">提供程序键。</param>
    /// <returns>是否受支持。</returns>
    public static bool IsSupportedProvider(string? providerKey) =>
        string.Equals(providerKey, ReportingDataSourceProviderKeys.SqlServer, StringComparison.Ordinal)
        || string.Equals(providerKey, ReportingDataSourceProviderKeys.MySql, StringComparison.Ordinal);

    /// <summary>解析提供程序默认端口。</summary>
    /// <param name="providerKey">提供程序键。</param>
    /// <returns>默认端口。</returns>
    public static int ResolveDefaultPort(string providerKey) =>
        string.Equals(providerKey, ReportingDataSourceProviderKeys.MySql, StringComparison.Ordinal)
            ? DefaultMySqlPort
            : DefaultSqlServerPort;

    /// <summary>校验结构化连接字段，禁止连接串分隔符越界注入。</summary>
    /// <param name="name">显示名称。</param>
    /// <param name="providerKey">提供程序键。</param>
    /// <param name="serverHost">服务器主机。</param>
    /// <param name="port">端口。</param>
    /// <param name="databaseName">数据库名。</param>
    /// <param name="username">用户名。</param>
    /// <returns>校验失败时的错误消息；成功时为 <see langword="null"/>。</returns>
    public static string? ValidateMetadata(
        string name,
        string providerKey,
        string serverHost,
        int port,
        string databaseName,
        string username)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return "Name is required and must not exceed 128 characters.";
        }

        if (!IsSupportedProvider(providerKey))
        {
            return "Provider key must be sql_server or mysql.";
        }

        if (!IsSafeHost(serverHost))
        {
            return "Server host is invalid or contains forbidden characters.";
        }

        if (port is < 1 or > 65535)
        {
            return "Port must be between 1 and 65535.";
        }

        if (!IsSafeIdentifier(databaseName))
        {
            return "Database name is invalid or contains forbidden characters.";
        }

        if (!IsSafeIdentifier(username))
        {
            return "Username is invalid or contains forbidden characters.";
        }

        return null;
    }

    private static bool IsSafeHost(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxHostLength)
        {
            return false;
        }

        return !ContainsConnectionStringDelimiters(normalized);
    }

    private static bool IsSafeIdentifier(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxIdentifierLength)
        {
            return false;
        }

        return !ContainsConnectionStringDelimiters(normalized);
    }

    private static bool ContainsConnectionStringDelimiters(string value) =>
        value.Contains(';', StringComparison.Ordinal)
        || value.Contains('=', StringComparison.Ordinal)
        || value.Contains('\r', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal);
}
