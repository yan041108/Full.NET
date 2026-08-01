namespace Full.NET.Hosting.Observability;

/// <summary>
/// 六档容量 Profile 的初始参考预算。正式定档留给 Task 14；此处只提供确定性采样候选。
/// </summary>
public static class HttpOperationLogProfile
{
    /// <summary>面向 10K 设计目标的初始参考档；不等于容量已认证。</summary>
    public const LoggingCapacityProfile DesignTargetProfile = LoggingCapacityProfile.XL;

    /// <summary>
    /// 返回成功请求的确定性采样率候选（0..1）。Ultra 默认接近零样本。
    /// </summary>
    public static double ResolveSuccessSampleRate(LoggingCapacityProfile profile) =>
        profile switch
        {
            LoggingCapacityProfile.S => 1.0,
            LoggingCapacityProfile.M => 0.25,
            LoggingCapacityProfile.L => 0.05,
            LoggingCapacityProfile.XL => 0.01,
            LoggingCapacityProfile.XXL => 0.001,
            LoggingCapacityProfile.Ultra => 0.0001,
            _ => 0.01,
        };

    /// <summary>
    /// 将粗略在途并发映射到档位；恰好 10K 属于 XL。仅用于文档/工具，不得驱动运行时自动切档。
    /// </summary>
    public static LoggingCapacityProfile MapConcurrentInFlight(int concurrentInFlight) =>
        concurrentInFlight switch
        {
            < 1_000 => LoggingCapacityProfile.S,
            < 5_000 => LoggingCapacityProfile.M,
            < 10_000 => LoggingCapacityProfile.L,
            < 50_000 => LoggingCapacityProfile.XL,
            < 100_000 => LoggingCapacityProfile.XXL,
            _ => LoggingCapacityProfile.Ultra,
        };
}
