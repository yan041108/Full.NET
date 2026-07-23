using System.Data.Common;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Health;

internal sealed class DatabaseConnectivityHealthCheck(
    DbConnectionFactory connectionFactory,
    IOptions<DatabaseOptions> databaseOptions) : IHealthCheck
{
    private static readonly TimeSpan MaximumProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        try
        {
            using var timeout = CreateTimeoutSource(cancellationToken);
            await using var connection = connectionFactory.Create();
            await connection.OpenAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = GetProbeTimeoutSeconds();
            _ = await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("数据库连接健康检查超时。");
        }
        catch (DbException)
        {
            return HealthCheckResult.Unhealthy("数据库连接健康检查失败。");
        }
        catch (InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("数据库连接健康检查失败。");
        }
    }

    private CancellationTokenSource CreateTimeoutSource(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(GetProbeTimeoutSeconds());
        if (timeout > MaximumProbeTimeout)
        {
            timeout = MaximumProbeTimeout;
        }

        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return timeoutSource;
    }

    private int GetProbeTimeoutSeconds() =>
        Math.Clamp(databaseOptions.Value.CommandTimeoutSeconds, 1, 5);
}
