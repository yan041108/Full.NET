namespace Full.NET.Abstractions.Ids;

/// <summary>
/// 定义全局唯一 ID 生成器的抽象契约，屏蔽具体算法（UUID v7、Snowflake 等）对调用方的影响。
/// </summary>
/// <remarks>
/// 实现应保证在分布式环境中可接受的碰撞概率，并尽可能提供时间有序或单调递增特性，
/// 以降低数据库索引碎片。调用方不得缓存或复用 <see cref="NewId"/> 返回值。
/// </remarks>
public interface IIdGenerator
{
    /// <summary>
    /// 生成一个新的全局唯一标识符。
    /// </summary>
    /// <returns>全局唯一的 <see cref="Guid"/>。</returns>
    Guid NewId();
}
