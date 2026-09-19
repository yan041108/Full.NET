using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Hosting.Serialization;

/// <summary>
/// 注册 Full.NET 宿主统一 JSON 序列化选项的扩展方法集合；统一使用 CamelCase 命名与 HostingJsonSerializerContext 源生成器。
/// </summary>
public static class FullNetJsonOptionsExtensions
{
    /// <summary>
    /// 注册 Full.NET 默认 HTTP JSON 选项：CamelCase 属性/字典键策略 + 在 TypeInfoResolverChain 头部插入源生成器上下文。
    /// </summary>
    /// <param name="services">宿主服务集合。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    /// <remarks>
    /// 通过 ConfigureHttpJsonOptions 配置 ASP.NET Core 默认 HttpJson 选项；影响所有使用 HttpJson 默认序列化器的 Minimal API 端点，不影响 MVC 控制器或显式传入 JsonSerializerOptions 的代码路径。
    /// </remarks>
    public static IServiceCollection AddFullNetJson(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                HostingJsonSerializerContext.Default);
        });

        return services;
    }
}
