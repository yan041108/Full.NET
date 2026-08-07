using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Files.Cleanup;
using Full.NET.Modules.Files.Contracts;
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
using Microsoft.Extensions.Hosting;
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
        RegisterStorage(services, configuration);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<Features.ManageHostFiles.HostFileQueryService>();
        services.TryAddScoped<Features.ManageHostFiles.HostFileManagementService>();
        services.TryAddScoped<IHostFileReferenceReader, Features.HostFileReferences.HostFileReferenceReader>();
        services.TryAddScoped<IHostFileContentReader, Features.HostFileReferences.HostFileContentReader>();
        services.TryAddScoped<IHostFileUploadWriter, Features.HostFileReferences.HostFileUploadWriter>();
        services.TryAddScoped<IHostFileReferenceClaimService, Features.HostFileReferenceClaims.HostFileReferenceClaimService>();
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
        RegisterStorage(services, configuration);
        services.AddOptions<DeletedHostFileBlobCleanupOptions>()
            .Bind(configuration.GetSection(DeletedHostFileBlobCleanupOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<PendingHostFileReconciliationOptions>()
            .Bind(configuration.GetSection(PendingHostFileReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<PendingHostFileReferenceClaimReconciliationOptions>()
            .Bind(configuration.GetSection(PendingHostFileReferenceClaimReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<DeletedHostFileBlobCleanupOptions>,
            DeletedHostFileBlobCleanupOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<PendingHostFileReconciliationOptions>,
            PendingHostFileReconciliationOptionsValidator>());
        services.TryAddScoped<DeletedHostFileBlobCleanupRunner>();
        services.TryAddScoped<PendingHostFileReconciliationRunner>();
        services.TryAddScoped<PendingHostFileReferenceClaimReconciliationRunner>();
        services.AddHostedService<DeletedHostFileBlobCleanupHostedProcessor>();
        services.AddHostedService<PendingHostFileReconciliationHostedProcessor>();
        services.AddHostedService<PendingHostFileReferenceClaimReconciliationHostedProcessor>();
    }

    private static void RegisterStorage(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LocalFileStorageOptions>()
            .Bind(configuration.GetSection(LocalFileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<S3FileStorageOptions>()
            .Bind(configuration.GetSection(S3FileStorageOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<LocalFileStorageOptions>,
            LocalFileStorageOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<FileStorageOptions>,
            FileStorageOptionsValidator>());
        // 注册时捕获默认 Provider，避免校验器依赖 IOptions<FileStorageOptions>/IConfiguration 造成启动环。
        var defaultProviderKey = configuration
                .GetSection(FileStorageOptions.SectionName)["DefaultProviderKey"]
            ?? LocalHostFileBlobStorage.Key;
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<S3FileStorageOptions>, S3FileStorageOptionsValidator>(
                sp => new S3FileStorageOptionsValidator(
                    sp.GetRequiredService<IHostEnvironment>(),
                    defaultProviderKey)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFileStorageProvider,
            LocalHostFileBlobStorage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFileStorageProvider,
            S3HostFileBlobStorage>());
        services.TryAddSingleton<FileStorageProviderRegistry>();
    }
}
