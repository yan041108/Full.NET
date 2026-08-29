using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.RateLimiting;
using Full.NET.Modules.Document.Resources;
using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Document.Serialization;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document;

/// <summary>
/// Host 文档管理模块入口：聚合文档项与版本、分类、标签、细粒度权限、匿名分享、统计与回收站，
/// 所有目录数据均以 TenantId IS NULL 的 Host 行存储；文件实体由 Files 模块托管，本模块只持有引用。
/// </summary>
public sealed class DocumentModule : IFullNetModule
{
    /// <summary>
    /// 匿名分享访问 POST 端点限流策略，按 IP 分区防止口令暴力与计数滥用。
    /// </summary>
    internal const string AnonymousShareAccessRateLimitPolicy = "document-anonymous-share-access";

    /// <summary>模块稳定键，用于 Composition Host Profile 排序与依赖解析。</summary>
    public string Name => "Document";

    /// <summary>
    /// 显式声明的运行时依赖：Identity 提供受信任用户与权限目录，Files 提供文件上传与引用 Claim 能力；
    /// 不得在未注册这两个模块的宿主中启用本模块。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Files"];

    /// <summary>
    /// 注册 Options 校验、限流策略、权限目录、错误资源、口令哈希、各 Feature 的 Scoped 查询/管理服务，
    /// 以及 Host 文件引用 Claim Probe；HTTP JSON 序列化注入本模块源生成上下文以保证 Native AOT 兼容。
    /// </summary>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.DocumentDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.AddOptions<DocumentOptions>()
            .Bind(configuration.GetSection(DocumentOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<DocumentOptions>,
            DocumentOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<RateLimiterOptions>,
            DocumentRateLimiterPolicyConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<RateLimitPolicyErrorCodes>,
            DocumentRateLimiterPolicyConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            DocumentAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            DocumentErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<IDocumentSharePasswordHasher, DocumentSharePasswordHasher>();
        services.TryAddScoped<Features.ManageHostDocumentItems.HostDocumentItemQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentItems.HostDocumentItemManagementService>();
        services.TryAddScoped<Features.ManageHostDocumentCategories.HostDocumentCategoryQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentCategories.HostDocumentCategoryManagementService>();
        services.TryAddScoped<Features.ManageHostDocumentTags.HostDocumentTagQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentTags.HostDocumentTagManagementService>();
        services.TryAddScoped<Features.QueryHostRecycleBin.HostRecycleBinQueryService>();
        services.TryAddScoped<Features.QueryHostRecycleBin.HostRecycleBinManagementService>();
        services.TryAddScoped<Features.ManageHostDocumentPermissions.HostDocumentPermissionQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentPermissions.HostDocumentPermissionManagementService>();
        services.TryAddScoped<Features.ManageHostDocumentShares.HostDocumentShareQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentShares.HostDocumentShareManagementService>();
        services.TryAddScoped<Features.QueryHostDocumentStatistics.HostDocumentStatisticsQueryService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileRetentionContributor,
            Features.HostFileReferences.DocumentHostFileRetentionContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileReferenceClaimProbe,
            Features.HostFileReferences.HostDocumentVersionReferenceProbe>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                DocumentJsonSerializerContext.Default));
    }

    /// <summary>
    /// 映射文档项、分类、标签、回收站、权限、分享与统计共 7 组 Host Endpoint；
    /// 每条端点必须显式声明精确权限策略，匿名分享访问端点额外绑定 IP 限流策略。
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostDocumentItems.Endpoint.Map(endpoints);
        Features.ManageHostDocumentCategories.Endpoint.Map(endpoints);
        Features.ManageHostDocumentTags.Endpoint.Map(endpoints);
        Features.QueryHostRecycleBin.Endpoint.Map(endpoints);
        Features.ManageHostDocumentPermissions.Endpoint.Map(endpoints);
        Features.ManageHostDocumentShares.Endpoint.Map(endpoints);
        Features.QueryHostDocumentStatistics.Endpoint.Map(endpoints);
    }

    /// <summary>
    /// 注册后台服务侧的 Host 文件引用 Claim Probe；与 API 端共享同一探测实现，
    /// 用于 Worker 触发的文件保留期回收与未确认引用清理事后对账。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.DocumentDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileReferenceClaimProbe,
            Features.HostFileReferences.HostDocumentVersionReferenceProbe>());
    }
}
