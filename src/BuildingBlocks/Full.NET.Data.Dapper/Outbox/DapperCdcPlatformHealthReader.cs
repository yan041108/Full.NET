using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>只读采集当前库 CDC 与保护状态；所有语句经过 Full.NET 超时、作用域及遥测边界。</summary>
internal sealed class DapperCdcPlatformHealthReader(IQueryExecutor query, IOptions<DatabaseOptions> database)
    : ICdcPlatformHealthReader
{
    public async Task<double?> ReadSourceTimeLagAsync(string sourceDatabaseName, CdcDeliveryPosition position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (database.Value.Provider == DatabaseProvider.SqlServer)
            return (await ReadSourcePositionAsync(sourceDatabaseName, position, cancellationToken).ConfigureAwait(false)).SourceLagSeconds;
        if (database.Value.Provider != DatabaseProvider.MySql || !position.IsValid()
            || position.Provider != CdcDeliveryPosition.MySqlProvider
            || !TryReadPrefix(position.Binlog?.CompletedGtidSet, out var source, out var captured)) return null;
        if (!string.Equals(sourceDatabaseName, await query.QuerySingleOrDefaultAsync<string>(MySqlDatabase,
            cancellationToken: cancellationToken).ConfigureAwait(false), StringComparison.Ordinal)) return null;
        var current = await query.QuerySingleOrDefaultAsync<string>(MySqlGtidHighWatermark,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (current is null) return null;
        var parts = current.Split('/');
        if (parts.Length != 2 || !string.Equals(parts[0], source, StringComparison.OrdinalIgnoreCase)
            || !TryReadPrefix(parts[1], out var currentSource, out var high)
            || !string.Equals(currentSource, source, StringComparison.OrdinalIgnoreCase) || high < captured) return null;
        // 精确覆盖当前事务高水位即可证明时间差为零，静默流不使用墙钟年龄。
        if (high == captured) return 0;
        var lag = await query.QuerySingleOrDefaultAsync<double?>(MySqlGtidTimeLag, DapperSqlParameters.Create(
            ("Captured", $"{source}:{captured.ToString(CultureInfo.InvariantCulture)}"),
            ("High", $"{source}:{high.ToString(CultureInfo.InvariantCulture)}")), cancellationToken).ConfigureAwait(false);
        return lag is >= 0 && double.IsFinite(lag.Value) ? lag : null;
    }

    // 只接受同一源 UUID 从 1 开始的完整连续前缀；多源、缺口、标签与过长输入均不能证明覆盖。
    private static bool TryReadPrefix(string? value, out string source, out long high)
    {
        source = string.Empty; high = 0;
        if (value is null || value.Length is < 38 or > 80 || value[36] != ':'
            || !Guid.TryParseExact(value[..36], "D", out var id)) return false;
        var range = value.AsSpan(37);
        if (range.SequenceEqual("1")) high = 1;
        else if (!range.StartsWith("1-", StringComparison.Ordinal)
            || !long.TryParse(range[2..], NumberStyles.None, CultureInfo.InvariantCulture, out high) || high <= 1) return false;
        source = id.ToString("D");
        return true;
    }

    public async Task<CdcSourcePositionHealthSnapshot> ReadSourcePositionAsync(string sourceDatabaseName, CdcDeliveryPosition position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (string.IsNullOrWhiteSpace(sourceDatabaseName) || !position.IsValid()) return new(null, null);
        var sqlServer = database.Value.Provider == DatabaseProvider.SqlServer;
        var actualDatabase = await query.QuerySingleOrDefaultAsync<string>(sqlServer ? SqlServerDatabase : MySqlDatabase,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!string.Equals(sourceDatabaseName, actualDatabase, sqlServer ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new(null, null);
        if (sqlServer && position.Provider == CdcDeliveryPosition.SqlServerProvider && position.Lsn is { } lsn)
        {
            var parameters = DapperSqlParameters.Create(("Position", Convert.FromHexString(lsn.CommitLsn.Replace(":", "", StringComparison.Ordinal))));
            var available = await query.QuerySingleOrDefaultAsync<int?>(SqlServerPositionAvailable, parameters, cancellationToken).ConfigureAwait(false);
            var lag = available == 1
                ? await query.QuerySingleOrDefaultAsync<double?>(SqlServerPositionLag, parameters, cancellationToken).ConfigureAwait(false) : null;
            return new(available is { } found ? found == 1 : null, lag is >= 0 ? lag : null);
        }
        if (database.Value.Provider == DatabaseProvider.MySql && position.Provider == CdcDeliveryPosition.MySqlProvider && position.Binlog is { } binlog)
        {
            var logs = await query.QueryAsync<(string Name, long Size, string Encrypted)>(MySqlBinaryLogs,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            // 平台元数据也受本轮超时预算约束；异常规模或加密文件不猜测逻辑坐标与物理文件头的关系。
            if (logs.Count is 0 or > 4096) return new(null, null);
            var log = logs.FirstOrDefault(item => string.Equals(item.Name, binlog.File, StringComparison.Ordinal));
            if (log.Name is null) return new(false, null);
            if (!string.Equals(log.Encrypted, "No", StringComparison.OrdinalIgnoreCase)) return new(null, null);
            return new(binlog.Position >= 4 && binlog.Position <= log.Size, null);
        }
        return new(null, null);
    }

    public async Task<CdcPlatformHealthSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var fenced = await query.QuerySingleOrDefaultAsync<int>(ProducerFence, cancellationToken: cancellationToken).ConfigureAwait(false) != 0;
        return database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => new(CdcDeliveryPosition.SqlServerProvider,
                await query.QuerySingleOrDefaultAsync<int?>(SqlServerCaptureJob, cancellationToken: cancellationToken).ConfigureAwait(false) is { } running
                    ? running != 0 : null, null, fenced),
            DatabaseProvider.MySql => new(CdcDeliveryPosition.MySqlProvider, null,
                await query.QuerySingleOrDefaultAsync<double?>(MySqlBinlogRetention, cancellationToken: cancellationToken).ConfigureAwait(false), fenced),
            _ => throw new NotSupportedException("CDC 平台采集只支持 SQL Server 与 MySQL。"),
        };
    }

    // 聚合只暴露保护是否存在；不在指标中使用流、租户、用户或操作 ID。
    private static readonly SqlStatement ProducerFence = new("messaging.cdc_health.producer_fence",
        "SELECT CASE WHEN EXISTS (SELECT 1 FROM fn_messaging_stream_ownership WHERE RollbackState = 1) THEN 1 ELSE 0 END",
        SqlDataScope.HostOnly);

    // 必须同时核对 Agent 进程和最新 Agent 会话中的当前库 Capture Job，避免已退出 Agent 留下的旧活动行假报健康。
    // 缺少 CDC 作业定义时返回 NULL；无 DMV/msdb 读取权限由执行器报错，采集端转换为 error。
    private static readonly SqlStatement SqlServerCaptureJob = new("messaging.cdc_health.capture_job.sqlserver",
        """
        SELECT CASE WHEN NOT EXISTS (
            SELECT 1 FROM msdb.dbo.cdc_jobs WHERE database_id = DB_ID() AND job_type = 'capture'
        ) THEN NULL WHEN EXISTS (
            SELECT 1 FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server Agent%' AND status = 4
        ) AND EXISTS (
            SELECT 1 FROM msdb.dbo.cdc_jobs jobs
            INNER JOIN msdb.dbo.sysjobactivity activity ON activity.job_id = jobs.job_id
            WHERE jobs.database_id = DB_ID() AND jobs.job_type = 'capture'
              AND activity.session_id = (SELECT MAX(session_id) FROM msdb.dbo.syssessions)
              AND activity.start_execution_date IS NOT NULL AND activity.stop_execution_date IS NULL
        ) THEN 1 ELSE 0 END
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement MySqlBinlogRetention = new("messaging.cdc_health.binlog_retention.mysql",
        "SELECT CASE WHEN @@global.log_bin = 1 THEN @@global.binlog_expire_logs_seconds / 3600.0 ELSE NULL END",
        SqlDataScope.HostOnly);

    private static readonly SqlStatement SqlServerDatabase = new("messaging.cdc_health.database.sqlserver", "SELECT DB_NAME()", SqlDataScope.HostOnly);
    private static readonly SqlStatement MySqlDatabase = new("messaging.cdc_health.database.mysql", "SELECT DATABASE()", SqlDataScope.HostOnly);
    private static readonly SqlStatement MySqlBinaryLogs = new("messaging.cdc_health.binary_logs.mysql", "SHOW BINARY LOGS", SqlDataScope.HostOnly);

    private static readonly SqlStatement MySqlGtidHighWatermark = new("messaging.cdc_health.gtid_high_watermark.mysql",
        "SELECT CASE WHEN @@GLOBAL.gtid_mode='ON' AND @@GLOBAL.log_bin=1 AND @@GLOBAL.binlog_order_commits=1 THEN CONCAT(@@GLOBAL.server_uuid, '/', @@GLOBAL.gtid_executed) ELSE NULL END",
        SqlDataScope.HostOnly);

    // 历史环无索引，限制为至多一万行；计时器关闭、历史淘汰、重复 GTID 或皮秒计数可能回绕时返回未知。
    // 只比较同服务器两个已完成事务的 TIMER_END，避免跨机时钟偏差和「距最后事件多久」误报。
    private static readonly SqlStatement MySqlGtidTimeLag = new("messaging.cdc_health.gtid_time_lag.mysql",
        """
        SELECT CASE WHEN @@performance_schema_events_transactions_history_long_size BETWEEN 1 AND 10000
          AND (SELECT VARIABLE_VALUE+0 FROM performance_schema.global_status WHERE VARIABLE_NAME='Uptime') < 18446743
          AND EXISTS (SELECT 1 FROM performance_schema.setup_consumers WHERE NAME='events_transactions_history_long' AND ENABLED='YES')
          AND EXISTS (SELECT 1 FROM performance_schema.setup_instruments WHERE NAME='transaction' AND ENABLED='YES' AND TIMED='YES')
          AND COUNT(CASE WHEN GTID=@Captured THEN 1 END)=1 AND COUNT(CASE WHEN GTID=@High THEN 1 END)=1
          THEN (MAX(CASE WHEN GTID=@High THEN TIMER_END END)-MAX(CASE WHEN GTID=@Captured THEN TIMER_END END))/1000000000000.0
          ELSE NULL END
        FROM (SELECT GTID,STATE,END_EVENT_ID,TIMER_END FROM performance_schema.events_transactions_history_long LIMIT 10000) history
        WHERE GTID IN (@Captured,@High) AND STATE='COMMITTED' AND END_EVENT_ID IS NOT NULL AND TIMER_END IS NOT NULL
        """, SqlDataScope.HostOnly);

    // min LSN 为零也可能表示权限不足，必须返回未知，不能当成无积压或已丢失来猜测。
    private static readonly SqlStatement SqlServerPositionAvailable = new("messaging.cdc_health.position.sqlserver",
        """
        DECLARE @Minimum binary(10) = sys.fn_cdc_get_min_lsn(N'fullnet_fn_messaging_outbox_event');
        DECLARE @Maximum binary(10) = sys.fn_cdc_get_max_lsn();
        SELECT CASE WHEN @Minimum=0x00000000000000000000 OR @Maximum=0x00000000000000000000 THEN NULL
          WHEN @Position < @Minimum OR @Position > @Maximum THEN 0 ELSE 1 END
        """, SqlDataScope.HostOnly);

    // 使用同一数据库时钟的源高水位差，避免静默流因「距离最后一条业务消息很久」被误报积压。
    private static readonly SqlStatement SqlServerPositionLag = new("messaging.cdc_health.source_lag.sqlserver",
        """
        SELECT DATEDIFF_BIG(MILLISECOND, sys.fn_cdc_map_lsn_to_time(@Position),
            sys.fn_cdc_map_lsn_to_time(sys.fn_cdc_get_max_lsn())) / 1000.0
        """, SqlDataScope.HostOnly);
}
