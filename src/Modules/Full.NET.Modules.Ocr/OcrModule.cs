using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ocr.Connectivity;
using Full.NET.Modules.Ocr.Features.ManageIdCardTasks;
using Full.NET.Modules.Ocr.Features.ManageProviderConfigs;
using Full.NET.Modules.Ocr.Security;
using Full.NET.Modules.Ocr.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Ocr;

/// <summary>提供 OCR Provider 配置与身份证识别上传、人工确认流程。</summary>
public sealed class OcrModule : IFullNetModule
{
    public string Name => "Ocr";

    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
        "Files",
    ];

    /// <summary>注册 OCR 模块服务，识别客户端通过接口暴露以便事务外调用可测试。</summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="configuration">宿主配置。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            OcrAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<OcrApiKeyProtector>();
        services.TryAddSingleton<PaddleOcrIdCardClient>();
        services.TryAddSingleton<IPaddleOcrIdCardClient>(static provider =>
            provider.GetRequiredService<PaddleOcrIdCardClient>());
        services.TryAddScoped<OcrProviderQueryService>();
        services.TryAddScoped<OcrProviderManagementService>();
        services.TryAddScoped<OcrProviderOperationsService>();
        services.TryAddScoped<OcrProviderSecretResolver>();
        services.TryAddScoped<OcrIdCardTaskQueryService>();
        services.TryAddScoped<OcrIdCardTaskService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                OcrJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageProviderConfigs.Endpoint.Map(endpoints);
        Features.ManageIdCardTasks.Endpoint.Map(endpoints);
    }
}
