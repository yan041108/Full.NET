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

/// <summary>
/// Files 业务模块入口：提供 Host 作用域文件上传、下载、引用声明（Claim）与已删除 Blob 回收能力。
/// </summary>
/// <remarks>
/// 安全边界：Host 文件元数据均以 <c>TenantId IS NULL</c> 表达 Host 作用域，SQL 声明 <c>SqlDataScope.HostOnly</c>；
/// 跨模块引用通过 <c>fn_files_file_reference_claim</c> 状态机声明，未释放的引用阻止软删除，回收仅清理无引用的已删除 Blob。
/// 后台清理与对账由 <c>AddBackgroundServices</c> 单独注册到 Worker 角色，API 宿主不隐式启动回收循环。
/// </remarks>
public sealed class FilesModule : IFullNetModule
{
    /// <summary>获取 Files 业务模块名称。</summary>
    public string Name => "Files";

    /// <summary>获取 Files 模块所需依赖；Identity 提供授权目录与 Host 用户解析。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <summary>
    /// 注册 Files 模块在 API 与 Worker 共用的 Host 文件/目录查询与管理服务、存储 Provider 目录、跨模块引用端口与 TenantResourceFileStore；
    /// 存储适配器（Local/S3/Oss）按配置注册到默认 Provider 集合，未配置凭据的 Provider 仍可注册但运行时拒绝写入。
    /// </summary>
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
        services.TryAddScoped<Features.ManageHostFolders.HostFolderQueryService>();
        services.TryAddScoped<Features.ManageHostFolders.HostFolderManagementService>();
        services.TryAddScoped<Features.ManageStorageProviders.FileStorageProviderCatalogService>();
        RegisterHostFileReferencePorts(services);
        services.TryAddScoped<ITenantResourceFileStore, Features.TenantResourceFiles.TenantResourceFileStore>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                FilesJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.FilesDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>映射 Files 模块文件、虚拟目录与存储 Provider 管理的全部受保护 HTTP 路由。</summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostFiles.Endpoint.Map(endpoints);
        Features.ManageHostFolders.Endpoint.Map(endpoints);
        Features.ManageStorageProviders.Endpoint.Map(endpoints);
    }

    /// <summary>
    /// 注册仅由 Worker 承载的文件后台任务，避免 API 角色隐式启动清理循环。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.FilesDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        RegisterStorage(services, configuration);
        RegisterHostFileReferencePorts(services);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddOptions<DeletedHostFileBlobCleanupOptions>()
            .Bind(configuration.GetSection(DeletedHostFileBlobCleanupOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<PendingHostFileReconciliationOptions>()
            .Bind(configuration.GetSection(PendingHostFileReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<PendingHostFileReferenceClaimReconciliationOptions>()
            .Bind(configuration.GetSection(PendingHostFileReferenceClaimReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<PendingTenantResourceFileReconciliationOptions>()
            .Bind(configuration.GetSection(PendingTenantResourceFileReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<DeletedHostFileBlobCleanupOptions>,
            DeletedHostFileBlobCleanupOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<PendingHostFileReconciliationOptions>,
            PendingHostFileReconciliationOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<PendingTenantResourceFileReconciliationOptions>,
            PendingTenantResourceFileReconciliationOptionsValidator>());
        services.TryAddScoped<DeletedHostFileBlobCleanupRunner>();
        services.TryAddScoped<PendingHostFileReconciliationRunner>();
        services.TryAddScoped<PendingHostFileReferenceClaimReconciliationRunner>();
        services.TryAddSingleton<TenantResourceFileReconciliationCursor>();
        services.TryAddScoped<PendingTenantResourceFileReconciliationRunner>();
        services.AddHostedService<DeletedHostFileBlobCleanupHostedProcessor>();
        services.AddHostedService<PendingHostFileReconciliationHostedProcessor>();
        services.AddHostedService<PendingHostFileReferenceClaimReconciliationHostedProcessor>();
        services.AddHostedService<PendingTenantResourceFileReconciliationHostedProcessor>();
    }

    /// <summary>跨模块文件引用契约；Worker 后台模块（Notifications/Document 等）依赖，须与 API AddServices 保持一致。</summary>
    private static void RegisterHostFileReferencePorts(IServiceCollection services)
    {
        services.TryAddScoped<IHostFileReferenceReader, Features.HostFileReferences.HostFileReferenceReader>();
        services.TryAddScoped<IHostFileDescriptorReader, Features.HostFileReferences.HostFileDescriptorReader>();
        services.TryAddScoped<IHostFileContentReader, Features.HostFileReferences.HostFileContentReader>();
        services.TryAddScoped<IHostFileUploadWriter, Features.HostFileReferences.HostFileUploadWriter>();
        services.TryAddScoped<IHostFileReferenceClaimService, Features.HostFileReferenceClaims.HostFileReferenceClaimService>();
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
        services.AddOptions<OssFileStorageOptions>()
            .Bind(configuration.GetSection(OssFileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddHttpClient(HttpOssBlobClient.HttpClientName);
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
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OssFileStorageOptions>, OssFileStorageOptionsValidator>(
                sp => new OssFileStorageOptionsValidator(
                    sp.GetRequiredService<IHostEnvironment>(),
                    defaultProviderKey)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFileStorageProvider,
            LocalHostFileBlobStorage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFileStorageProvider,
            S3HostFileBlobStorage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFileStorageProvider,
            OssHostFileBlobStorage>());
        services.TryAddSingleton<FileStorageProviderRegistry>();
    }
}
