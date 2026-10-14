namespace Full.NET.Data.Abstractions;

/// <summary>仅供独立权威授权边界使用的连接预留，不提高普通业务查询或 Worker 操作的优先级。</summary>
public interface IDatabaseAuthorizationReadScope
{
    /// <summary>宿主准入和实际连接池是否为事务外独立授权留下容量。</summary>
    bool HasIndependentReadCapacity { get; }

    /// <summary>当前独立数据作用域是否正在读取权威授权，供边界保留容量拒绝语义。</summary>
    bool IsActive { get; }

    /// <summary>在独立数据作用域内进入授权预留；未配置容量时立即拒绝，不回退普通或 Worker 通道。</summary>
    /// <returns>按后进先出释放的范围。</returns>
    IDisposable Enter();
}
