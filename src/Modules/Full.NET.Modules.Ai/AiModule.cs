using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Ai.Connectivity;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Features.ManageModelConfigs;
using Full.NET.Modules.Ai.Features.ManageTenantQuotas;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Ai;

/// <summary>提供 AI 模型配置、连通性测试与租户配额管理 API。</summary>
public sealed class AiModule : IFullNetModule
{
    public string Name => "Ai";

    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
    ];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            AiAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<AiApiKeySecretProtector>();
        services.AddHttpClient(AiModelConnectivityTester.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient(AiChatCompletionStreamer.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(3));
        services.TryAddSingleton<AiModelConnectivityTester>();
        services.TryAddSingleton<AiChatGenerationRegistry>();
        services.TryAddSingleton<AiChatCompletionStreamer>();
        services.TryAddSingleton<AiChatQuotaGuard>();
        services.TryAddScoped<AiModelConfigQueryService>();
        services.TryAddScoped<AiModelConfigManagementService>();
        services.TryAddScoped<AiModelConfigOperationsService>();
        services.TryAddScoped<AiTenantQuotaQueryService>();
        services.TryAddScoped<AiTenantQuotaManagementService>();
        services.TryAddScoped<AiChatSessionQueryService>();
        services.TryAddScoped<AiChatSessionManagementService>();
        services.TryAddScoped<AiChatStreamService>();
        services.TryAddScoped<AiAgentToolCallQueryService>();
        services.TryAddScoped<AiAgentToolCallAuditWriter>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                AiJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageModelConfigs.Endpoint.Map(endpoints);
        Features.ManageTenantQuotas.Endpoint.Map(endpoints);
        Features.ManageChatSessions.Endpoint.Map(endpoints);
        Features.ManageAgentTools.Endpoint.Map(endpoints);
    }
}
