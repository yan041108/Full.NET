using System.Data.Common;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Health;

/// <summary>
/// 数据库 Schema Contract 健康检查，实现 <see cref="IHealthCheck"/>，
/// 通过查询 <c>fn_uuid_contract_state</c> 表的 SchemaMode 列验证数据库迁移（Migration）
/// 是否已执行至与当前应用代码契约兼容的版本。属于 Startup Probe（tag: startup）。
/// </summary>
/// <remarks>
/// <para><b>设计意图：</b>防止应用已部署新版本但数据库 Migrator 尚未执行最新脚本，导致运行时
/// 出现"列不存在"、"表不存在"等破坏性错误。与 <see cref="MySqlSchemaModeStartupValidator"/>
/// （HostedService 启动门禁）互为补充：本检查提供 K8s / 编排层可见的健康端点，
/// Validator 则在应用启动早期 Fail Fast。</para>
/// <para><b>判定规则：</b>SchemaMode 非空字符串即视为契约就绪。空或 NULL 表示
/// fn_uuid_contract_state 表为空或 Migrator 未写入契约版本。</para>
/// <para><b>Provider 差异：</b>SQL Server 使用 TOP (1) + dbo schema；MySQL 使用 LIMIT 1。</para>
/// <para><b>超时策略：</b>同连通性检查，超时 Clamp 至 [1, 5] 秒。</para>
/// </remarks>
internal sealed class DatabaseSchemaHealthCheck(
    DbConnectionFactory connectionFactory,
    IOptions<DatabaseOptions> databaseOptions) : IHealthCheck
{
    /// <summary>
    /// 执行 Schema Contract 检查：查询 fn_uuid_contract_state 表的 SchemaMode 列并判定是否非空。
    /// </summary>
    /// <param name="context">健康检查上下文（本实现不使用）。</param>
    /// <param name="cancellationToken">上游取消令牌。</param>
    /// <returns>Healthy（契约就绪）或 Unhealthy（未就绪/超时/异常）。</returns>
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
