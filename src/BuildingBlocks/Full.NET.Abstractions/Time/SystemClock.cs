namespace Full.NET.Abstractions.Time;

/// <summary>
/// 基于操作系统时钟的 <see cref="IClock"/> 实现，委托给 <see cref="DateTimeOffset.UtcNow"/>。
/// </summary>
/// <remarks>
/// 该实现为生产环境默认配置；线程安全，可作为 Singleton 使用。
/// 测试环境应替换为可控的假时钟实现。
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <summary>
    /// 获取当前操作系统的 UTC 时间。
    /// </summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
