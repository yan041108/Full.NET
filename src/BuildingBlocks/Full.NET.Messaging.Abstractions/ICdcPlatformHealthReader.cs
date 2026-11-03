namespace Full.NET.Messaging.Abstractions;

/// <summary>通过数据执行边界读取当前部署的 CDC 平台健康；不执行修复或修改源位点。</summary>
public interface ICdcPlatformHealthReader
{
    /// <summary>读取当前数据库提供程序的实际健康样本；权限不足和连接失败须传播，不能返回健康。</summary>
    /// <param name="cancellationToken">限制数据库采集期限的取消令牌。</param>
    /// <returns>仅包含当前提供程序适用字段的样本；未知字段为 null。</returns>
    Task<CdcPlatformHealthSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>核对专用源库仍保留 Connector 已提交的源坐标；缺权限、无有效窗口或提供程序不匹配时不能报告可恢复。</summary>
    /// <param name="sourceDatabaseName">配置绑定的精确源库名，必须与当前连接的实际数据库一致。</param>
    /// <param name="position">Connect 返回的真实源坐标。</param>
    /// <param name="cancellationToken">本轮采集预算。</param>
    /// <returns>可恢复性及能够从数据库有效区间证明的源积压时间；未知字段为 null。</returns>
    Task<CdcSourcePositionHealthSnapshot> ReadSourcePositionAsync(string sourceDatabaseName, CdcDeliveryPosition position,
        CancellationToken cancellationToken = default);

    /// <summary>可选的提交检查点时间差观测；其失败不得改变位点可恢复判断。MySQL 需要完整 GTID 前缀和仍保留的事务计时。</summary>
    /// <param name="sourceDatabaseName">绑定的精确源库名。</param>
    /// <param name="position">Connect 已持久化的源检查点。</param>
    /// <param name="cancellationToken">本轮采集预算。</param>
    /// <returns>同源已提交高水位的时间差秒数；不支持、证据不足时为 null，权限和连接错误传播。</returns>
    Task<double?> ReadSourceTimeLagAsync(string sourceDatabaseName, CdcDeliveryPosition position,
        CancellationToken cancellationToken = default);
}

/// <summary>实际源日志有效区间的只读判断，不执行清理、重置或位点变更。</summary>
/// <param name="Recoverable">坐标处于当前可读源日志窗口；无法证明时为 null。</param>
/// <param name="SourceLagSeconds">当前源高水位与 Connector 坐标的提交时间差；不是距最近一条业务事件的墙钟年龄。</param>
public sealed record CdcSourcePositionHealthSnapshot(bool? Recoverable, double? SourceLagSeconds);

/// <summary>单次数据库平台观测；未知与不适用均不提供数值，调用方按 Provider 区分。</summary>
/// <param name="Provider">稳定提供程序码 sqlserver 或 mysql。</param>
/// <param name="CaptureJobRunning">SQL Server Agent 及当前库 Capture Job 是否实际运行。</param>
/// <param name="BinlogRetentionHours">MySQL 配置的自动过期小时数；0 表示未启用自动过期，不代表保留不足。</param>
/// <param name="ProducerFenced">当前数据库是否有持久 producer fence；不返回事件或租户身份。</param>
public sealed record CdcPlatformHealthSnapshot(string Provider, bool? CaptureJobRunning,
    double? BinlogRetentionHours, bool ProducerFenced);
