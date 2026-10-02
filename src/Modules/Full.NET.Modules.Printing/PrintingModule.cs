using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Features.BrowseFormSchemas;
using Full.NET.Modules.Printing.Features.ManageTemplates;
using Full.NET.Modules.Printing.Features.PreviewTemplates;
using Full.NET.Modules.Printing.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Printing;

/// <summary>提供固定表单 Schema、打印模板版本与浏览器预览绑定 API。</summary>
public sealed class PrintingModule : IFullNetModule
{
    /// <summary>获取 Printing 模块在依赖图中的唯一稳定名称。</summary>
    public string Name => "Printing";

    /// <summary>获取 Printing 模块运行所需的模块依赖；Identity 提供授权目录与身份上下文，Tenancy 提供租户上下文。</summary>
    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
    ];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            PrintingAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<PrintingFormSchemaQueryService>();
        services.TryAddScoped<PrintingTemplateQueryService>();
        services.TryAddScoped<PrintingTemplateManagementService>();
        services.TryAddScoped<PrintingFormBindingService>();
        services.TryAddScoped<PrintingTemplatePreviewService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                PrintingJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.BrowseFormSchemas.Endpoint.Map(endpoints);
        Features.ManageTemplates.Endpoint.Map(endpoints);
        Features.PreviewTemplates.Endpoint.Map(endpoints);
    }
}
