using Full.NET.Abstractions.Ids;

using Full.NET.Abstractions.Time;

using Full.NET.Hosting.Api;

using Full.NET.Modularity.Modules;

using Full.NET.Modules.Files.Cleanup;

using Full.NET.Modules.Files.Resources;
using Full.NET.Modules.Files.Reconciliation;

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
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<FileStorageOptions>,
            FileStorageOptionsValidator>());

        services.TryAddSingleton<IClock, SystemClock>();

        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFileStorageProvider,
            LocalHostFileBlobStorage>());
        services.TryAddSingleton<FileStorageProviderRegistry>();

        services.TryAddScoped<Features.ManageHostFiles.HostFileQueryService>();

        services.TryAddScoped<Features.ManageHostFiles.HostFileManagementService>();

        services.ConfigureHttpJsonOptions(options =>

            options.SerializerOptions.TypeInfoResolverChain.Insert(

                0,

                FilesJsonSerializerContext.Default));

    }



    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>

        Features.ManageHostFiles.Endpoint.Map(endpoints);

    /// <summary>
    /// 注册仅由 Worker 承载的文件后台任务，避免 API 角色隐式启动清理循环。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LocalFileStorageOptions>()
            .Bind(configuration.GetSection(LocalFileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<DeletedHostFileBlobCleanupOptions>()
            .Bind(configuration.GetSection(DeletedHostFileBlobCleanupOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<PendingHostFileReconciliationOptions>()
            .Bind(configuration.GetSection(PendingHostFileReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<DeletedHostFileBlobCleanupOptions>,
            DeletedHostFileBlobCleanupOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<PendingHostFileReconciliationOptions>,
            PendingHostFileReconciliationOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<LocalFileStorageOptions>,
            LocalFileStorageOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<FileStorageOptions>,
            FileStorageOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFileStorageProvider,
            LocalHostFileBlobStorage>());
        services.TryAddSingleton<FileStorageProviderRegistry>();
        services.TryAddScoped<DeletedHostFileBlobCleanupRunner>();
        services.TryAddScoped<PendingHostFileReconciliationRunner>();
        services.AddHostedService<DeletedHostFileBlobCleanupHostedProcessor>();
        services.AddHostedService<PendingHostFileReconciliationHostedProcessor>();
    }

}
