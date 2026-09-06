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
    public string Name => "Printing";

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
