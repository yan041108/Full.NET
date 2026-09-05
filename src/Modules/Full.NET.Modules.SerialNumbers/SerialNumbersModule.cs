using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.AllocateSerialNumbers;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.SerialNumbers;

/// <summary>
/// Host 流水号模块入口：提供规则目录管理（CRUD、启停、预览）与数据库原子分配能力。
/// 显式声明 Identity 依赖，因为规则变更审计与权限校验依赖 Identity 的受信任用户目录；
/// 不依赖 Files 等其他模块，是流水号能力的最小可独立组合单元。
/// 关键并发安全由 SerialNumberAllocator 与 SerialNumberSql.Allocate* 系列语句在数据库事务内保证。
/// </summary>
public sealed class SerialNumbersModule : IFullNetModule
{
    /// <summary>模块稳定键，用于 Composition Host Profile 排序与依赖解析。</summary>
    public string Name => "SerialNumbers";

    /// <summary>
    /// 显式声明的运行时依赖：Identity 提供受信任用户与权限目录；
    /// 不得在未注册 Identity 模块的宿主中启用本模块。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <summary>
    /// 注册权限目录贡献者、IClock/IIdGenerator 单例（如未由其他模块提供）、
    /// 规则管理服务与分配服务的 Scoped 生命周期，并向 HTTP JSON 序列化注入本模块源生成上下文
    /// 以保证 Native AOT 兼容。SerialNumberAllocator 必须为 Scoped 以访问 ICurrentTenant。
    /// </summary>
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.SerialNumbersDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            SerialNumbersAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<SerialNumberPreviewService>();
        services.TryAddScoped<HostSerialRuleService>();
        services.TryAddScoped<ISerialNumberAllocator, SerialNumberAllocator>();
        services.TryAddScoped<ISerialRuleChangeApprovalSource, Features.DataApprovalBridge.SerialRuleChangeApprovalSource>();
        services.TryAddScoped<ISerialRuleChangeApprovalApplier, Features.DataApprovalBridge.SerialRuleChangeApprovalApplier>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                SerialNumbersJsonSerializerContext.Default));
    }

    /// <summary>
    /// 映射规则目录管理与预览的 Host Endpoint；每条端点必须显式声明精确权限策略，
    /// 流水号分配能力通过 ISerialNumberAllocator 由其他模块（如订单、合同）间接调用，不暴露 HTTP 端点。
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Endpoint.Map(endpoints);
    }
}
