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

public sealed class DocumentModule : IFullNetModule
{
    /// <summary>
    /// 匿名分享访问 POST 端点限流策略，按 IP 分区防止口令暴力与计数滥用。
    /// </summary>
    internal const string AnonymousShareAccessRateLimitPolicy = "document-anonymous-share-access";

    public string Name => "Document";

    public IReadOnlyCollection<string> Dependencies => ["Identity", "Files"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
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

    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileReferenceClaimProbe,
            Features.HostFileReferences.HostDocumentVersionReferenceProbe>());
    }
}
