using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Document.Resources;
using Full.NET.Modules.Document.Serialization;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Document;

public sealed class DocumentModule : IFullNetModule
{
    public string Name => "Document";

    public IReadOnlyCollection<string> Dependencies => ["Identity", "Files"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            DocumentAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            DocumentErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<Features.ManageHostDocumentItems.HostDocumentItemQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentItems.HostDocumentItemManagementService>();
        services.TryAddScoped<Features.ManageHostDocumentCategories.HostDocumentCategoryQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentCategories.HostDocumentCategoryManagementService>();
        services.TryAddScoped<Features.ManageHostDocumentTags.HostDocumentTagQueryService>();
        services.TryAddScoped<Features.ManageHostDocumentTags.HostDocumentTagManagementService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileRetentionContributor,
            Features.HostFileReferences.DocumentHostFileRetentionContributor>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                DocumentJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostDocumentItems.Endpoint.Map(endpoints);
        Features.ManageHostDocumentCategories.Endpoint.Map(endpoints);
        Features.ManageHostDocumentTags.Endpoint.Map(endpoints);
    }
}
