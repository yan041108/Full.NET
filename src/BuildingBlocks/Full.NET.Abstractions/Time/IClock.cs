namespace Full.NET.Abstractions.Time;

/// <summary>
/// 系统时钟的可测试抽象，屏蔽对 <see cref="DateTimeOffset.UtcNow"/> 等静态时间源的直接依赖。
/// </summary>
/// <remarks>
/// 业务代码应始终通过该接口获取当前时间，以便单元测试可注入固定时间或手动推进时钟，
/// 从而稳定地测试超时、缓存过期、调度窗口等时间敏感逻辑。
/// </remarks>
public interface IClock
{
    /// <summary>
    /// 获取当前 UTC 时间。
    /// </summary>
    /// <value>以 UTC 时区表示的当前 <see cref="DateTimeOffset"/>。</value>
    DateTimeOffset UtcNow { get; }
}
