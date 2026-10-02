using System.Text.Json.Serialization.Metadata;

namespace Full.NET.Caching.Fusion.Serialization;

/// <summary>
/// 为 FusionCache L2 提供 Native AOT 可达的源生成 JSON 元数据。
/// 载荷所有者必须在自身程序集贡献元数据，缓存基础设施不得反向引用业务模块。
/// </summary>
public interface ICacheJsonTypeInfoContributor
{
    /// <summary>
    /// 返回指定载荷的源生成元数据；不属于当前贡献者的类型返回 <see langword="null"/>。
    /// </summary>
    /// <param name="type">待查询 JSON 元数据的载荷类型；由缓存基础设施按当前缓存键的运行时类型传入。</param>
    /// <returns>
    /// 当前贡献者为 <paramref name="type"/> 提供的源生成 <see cref="JsonTypeInfo"/>；
    /// 若该类型不属于本贡献者负责的载荷集合，则返回 <see langword="null"/>，由缓存基础设施继续向下一贡献者查询。
    /// </returns>
    JsonTypeInfo? GetTypeInfo(Type type);
}
