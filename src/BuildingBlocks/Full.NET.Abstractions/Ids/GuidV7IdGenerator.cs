namespace Full.NET.Abstractions.Ids;

/// <summary>
/// 基于 UUID v7（RFC 9562）的 ID 生成器，按 UTC 时间排序，适用于作为数据库主键以减少索引碎片。
/// </summary>
/// <remarks>
/// 该实现直接委托给 <see cref="Guid.CreateVersion7()"/>，使用系统时钟作为时间源；
/// 在同一毫秒内的并发生成由算法内部随机位保证唯一性。线程安全，可作为 Singleton 使用。
/// </remarks>
public sealed class GuidV7IdGenerator : IIdGenerator
{
    /// <summary>
    /// 生成一个符合 UUID v7 规范的新 <see cref="Guid"/>，其前 48 位编码当前 UTC 时间戳。
    /// </summary>
    /// <returns>时间排序的全局唯一 <see cref="Guid"/>。</returns>
    public Guid NewId() => Guid.CreateVersion7();
}
