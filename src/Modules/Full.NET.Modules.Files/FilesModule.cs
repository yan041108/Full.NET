using Full.NET.Abstractions.Ids;

using Full.NET.Abstractions.Time;

using Full.NET.Hosting.Api;

using Full.NET.Modularity.Modules;

using Full.NET.Modules.Files.Resources;

using Full.NET.Modules.Files.Serialization;

using Full.NET.Modules.Files.Storage;

using Full.NET.Modules.Identity.Contracts;

using Microsoft.AspNetCore.Builder;

using Microsoft.AspNetCore.Routing;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;



namespace Full.NET.Modules.Files;



public sealed class FilesModule : IFullNetModule

{

    public string Name => "Files";



    public IReadOnlyCollection<string> Dependencies => ["Identity"];



    public void AddServices(

        IServiceCollection services,

        IConfiguration configuration)

    {

        services.TryAddEnumerable(ServiceDescriptor.Singleton<

            IAuthorizationCatalogContributor,

            FilesAuthorizationContributor>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<

            IErrorResourceSource,

            FilesErrorResourceSource>());

        services.AddOptions<LocalFileStorageOptions>()

            .Bind(configuration.GetSection(LocalFileStorageOptions.SectionName))

            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<

            IValidateOptions<LocalFileStorageOptions>,

            LocalFileStorageOptionsValidator>());

        services.TryAddSingleton<IClock, SystemClock>();

        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();

        services.TryAddSingleton<IHostFileBlobStorage, LocalHostFileBlobStorage>();

        services.TryAddScoped<Features.ManageHostFiles.HostFileQueryService>();

        services.TryAddScoped<Features.ManageHostFiles.HostFileManagementService>();

        services.ConfigureHttpJsonOptions(options =>

            options.SerializerOptions.TypeInfoResolverChain.Insert(

                0,

                FilesJsonSerializerContext.Default));

    }



    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>

        Features.ManageHostFiles.Endpoint.Map(endpoints);

}

