using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper;

/// <summary>外部数据库出站目标由部署配置显式授权，默认不允许连接。</summary>
public sealed class ExternalDatabaseAccessOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:ExternalDatabaseAccess";

    /// <summary>
    /// 显式授权的外部数据库目标列表；默认为空，即拒绝所有外部数据库出站连接。
    /// </summary>
    public ExternalDatabaseDestination[] AllowedDestinations { get; set; } = [];

    /// <summary>外部数据库并发会话上限；默认 16，用于防止出站连接耗尽资源。</summary>
    public int MaxConcurrentSessions { get; set; } = 16;

    /// <summary>准入等待超时秒数；默认 1，超过后拒绝新的外部会话请求。</summary>
    public int AdmissionTimeoutSeconds { get; set; } = 1;
}

/// <summary>精确主机、端口与提供程序授权；证书豁免必须单独声明。</summary>
public sealed class ExternalDatabaseDestination
{
    /// <summary>目标数据库提供程序类型（如 SqlServer、MySql）。</summary>
    public DatabaseProvider Provider { get; set; }

    /// <summary>授权的目标主机名。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>授权的目标端口号。</summary>
    public int Port { get; set; }

    /// <summary>
    /// 是否允许不受信证书；仅对 SQL Server 生效，默认 false，开启前须确认证书豁免风险。
    /// </summary>
    public bool AllowUntrustedCertificate { get; set; }
}

internal static class ExternalDatabaseAccessPolicy
{
    internal static bool IsAllowed(
        ExternalDatabaseConnectionRequest request,
        ExternalDatabaseAccessOptions options) =>
        options.AllowedDestinations.Any(destination =>
            destination.Provider == request.Provider
            && string.Equals(destination.Host, request.ServerHost, StringComparison.OrdinalIgnoreCase)
            && destination.Port == request.Port
            && (request.Provider != DatabaseProvider.SqlServer
                || !request.TrustServerCertificate
                || destination.AllowUntrustedCertificate));
}
