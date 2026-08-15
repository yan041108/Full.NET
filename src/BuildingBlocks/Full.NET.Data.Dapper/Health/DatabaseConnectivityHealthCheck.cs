using System.Data.Common;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Health;

/// <summary>
/// 数据库连接连通性健康检查（Connectivity Probe），
/// 实现 <see cref="IHealthCheck"/>，通过 SELECT 1 验证目标数据库是否可访问、
/// 连接池是否可用以及网络是否正常。属于 Readiness Probe（tag: ready）。
/// </summary>
/// <remarks>
/// <para><b>超时策略：</b>超时时间取 <see cref="DatabaseOptions.CommandTimeoutSeconds"/> 并 Clamp 至 [1, 5] 秒，
/// 通过 <see cref="CancellationTokenSource.CreateLinkedTokenSource"/> + CancelAfter 强制超时，
/// 避免健康检查因网络或数据库 hang 住导致探针永远不返回。硬上限 5 秒（MaximumProbeTimeout）。</para>
/// <para><b>异常映射：</b>
/// <list type="bullet">
/// <item>OperationCanceledException（非上游取消）→ Unhealthy：连接超时。</item>
/// <item>DbException / InvalidOperationException → Unhealthy：连接或 SQL 执行失败。</item>
/// <item>其他异常 → 向上抛出，由健康检查框架统一记录为 Degraded/Unhealthy。</item>
/// </list>
/// </para>
/// <para><b>无泄漏保证：</b>连接、命令、超时 CTS 均包裹在 await using / using 中，失败路径亦保证释放。</para>
/// </remarks>
internal sealed class DatabaseConnectivityHealthCheck(
    DbConnectionFactory connectionFactory,
    IOptions<DatabaseOptions> databaseOptions) : IHealthCheck
{
    private static readonly TimeSpan MaximumProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 执行连通性检查：创建新连接 → Open → SELECT 1。
    /// </summary>
    /// <param name="context">健康检查上下文（本实现不使用，保留以符合接口契约）。</param>
    /// <param name="cancellationToken">上游取消令牌；内部会叠加超时令牌。</param>
    /// <returns>Healthy（连通正常）或 Unhealthy（超时/异常）。</returns>
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
