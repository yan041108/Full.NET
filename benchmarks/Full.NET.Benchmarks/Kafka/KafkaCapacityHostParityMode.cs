namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// Capacity Runner 与 Worker 宿主 DI 的对齐模式。
/// </summary>
public enum KafkaCapacityHostParityMode
{
    /// <summary>
    /// 合成订阅与宽松所有权门控；用于开发迭代，<strong>非</strong>切流证据。
    /// </summary>
    Fast = 0,

    /// <summary>
    /// 复用生产 Dapper 所有权解析与门控；仍使用容量专用事件类型与样本编排。
    /// </summary>
    WorkerParity = 1,
}
