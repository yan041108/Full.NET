using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper;

/// <summary>外部数据库出站目标由部署配置显式授权，默认不允许连接。</summary>
public sealed class ExternalDatabaseAccessOptions
{
    public const string SectionName = "FullNet:ExternalDatabaseAccess";

    public ExternalDatabaseDestination[] AllowedDestinations { get; set; } = [];

    public int MaxConcurrentSessions { get; set; } = 16;

    public int AdmissionTimeoutSeconds { get; set; } = 1;
}

/// <summary>精确主机、端口与提供程序授权；证书豁免必须单独声明。</summary>
public sealed class ExternalDatabaseDestination
{
    public DatabaseProvider Provider { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

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
