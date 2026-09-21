using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using OpenTelemetry.Metrics;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Ai.Connectivity;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Features.ManageAgentDelegations;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Features.ManageModelConfigs;
using Full.NET.Modules.Ai.Features.TestEmbeddings;
using Full.NET.Modules.Ai.Features.ManageMcpRemoteConnections;
using Full.NET.Modules.Ai.Features.ManageTenantQuotas;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.AgenticWeb.AgUi;
using Full.NET.AgenticWeb.Mcp.Client;
using Full.NET.AgenticWeb.Mcp.Server;
using Full.NET.Agents.Mcp;
using Full.NET.Modules.Ai.Mcp;
using Full.NET.Agents.AgUi;
using Full.NET.Modules.Ai.AgUi;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        services.TryAddScoped<AiModelBindingScope>();
        services.TryAddScoped<IProtectedModelCredentialStore>(provider => provider.GetRequiredService<AiModelBindingScope>());
        services.TryAddScoped<AiModelConnectivityTester>();
        services.TryAddSingleton<AiChatGenerationRegistry>();
        services.TryAddSingleton<AiChatGenerationLeaseMonitor>();
        services.TryAddSingleton<AiChatCleanupScope>();
        services.TryAddScoped<AiChatCompletionStreamer>();
#if FULLNET_AOT_COMPILE
        Persistence.AiBudgetRowReaders.Register();
#endif
        services.TryAddScoped<AiChatQuotaGuard>();
        services.AddOptions<AiOperationBudgetOptions>().BindConfiguration("FullNet:Ai:Budgets");
        services.TryAddScoped<IAiOperationBudgetStore, AiOperationBudgetStore>();
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(AiOperationBudgetStore.MeterName));
        services.TryAddScoped<AiModelConfigQueryService>();
        services.TryAddScoped<AiModelConfigManagementService>();
        services.TryAddScoped<AiModelConfigOperationsService>();
        services.TryAddScoped<AiEmbeddingTestService>();
        services.TryAddScoped<AiMcpRemoteConnectionQueryService>();
        services.TryAddScoped<AiMcpRemoteConnectionManagementService>();
        services.TryAddScoped<AiTenantQuotaQueryService>();
        services.TryAddScoped<AiTenantQuotaManagementService>();
        services.TryAddScoped<AiChatSessionQueryService>();
        services.TryAddScoped<AiChatSessionManagementService>();
        services.TryAddScoped<AiChatStreamService>();
        services.TryAddScoped<AiAgentToolCallQueryService>();
        RegisterAgentToolExecution(services, forWorker: false);
        services.TryAddScoped<AiAgentRunApprovalGate>();
        services.TryAddScoped<AiAgentApprovalConsumption>();
        services.TryAddScoped<AiAgentApprovalService>();
        services.TryAddScoped<AiAgentDelegationService>();
        services.TryAddScoped<IAgentRunStore, AiAgentRunStore>();
        services.TryAddScoped<AiAgentRunStore>();
        services.AddOptions<AiAgentRuntimeOptions>().BindConfiguration(AiAgentRuntimeOptions.SectionName);
        // API 侧就绪门禁需要读取 StaleThreshold；Worker 侧复用同一单例写入心跳。
        services.TryAddSingleton<AiAgentWorkerHeartbeatService>();
        services.TryAddScoped<AiAgentRunReadiness>();
        services.TryAddScoped<AiAgentRunManagementService>();
        services.TryAddScoped<AiAgentRunQueryService>();
        services.TryAddScoped<IAgentRunAgUiReader, AiAgentRunAgUiReader>();
        services.TryAddScoped<AgUiStreamService>();
        services.AddFullNetMcpServer(configuration);
        services.AddFullNetMcpClient(configuration);
        services.TryAddScoped<IMcpToolExposureCatalog, AiMcpToolExposureCatalog>();
        services.TryAddScoped<IMcpSessionSummaryReader, AiMcpSessionSummaryReader>();
        services.TryAddScoped<IMcpRemoteToolCatalog, AiMcpRemoteToolCatalog>();
        services.TryAddScoped<AiMcpRemoteTokenProtector>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                AiJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageModelConfigs.Endpoint.Map(endpoints);
        Features.TestEmbeddings.Endpoint.Map(endpoints);
        Features.ManageMcpRemoteConnections.Endpoint.Map(endpoints);
        Features.ManageTenantQuotas.Endpoint.Map(endpoints);
        Features.ManageChatSessions.Endpoint.Map(endpoints);
        Features.ManageAgentTools.Endpoint.Map(endpoints);
        Features.ManageAgentRuns.Endpoint.Map(endpoints);
        AgUiEndpoint.Map(endpoints);
        endpoints.MapFullNetMcp();
        Features.ManageAgentApprovals.Endpoint.Map(endpoints);
        Features.ManageAgentDelegations.Endpoint.Map(endpoints);
    }

    /// <summary>只在 Worker 注册领取循环；Provider 由 Composition 在 Worker Profile 装配。</summary>
    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiAgentRuntimeOptions>().BindConfiguration(AiAgentRuntimeOptions.SectionName);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<AiModelBindingScope>();
        services.TryAddScoped<IProtectedModelCredentialStore>(provider => provider.GetRequiredService<AiModelBindingScope>());
        services.TryAddSingleton<AiChatGenerationRegistry>();
        services.TryAddScoped<AiChatQuotaGuard>();
        services.AddOptions<AiOperationBudgetOptions>().BindConfiguration("FullNet:Ai:Budgets");
        services.TryAddScoped<AiChatSessionQueryService>();
        services.TryAddScoped<AiChatSessionManagementService>();
        services.TryAddScoped<AiAgentRunStore>();
        services.TryAddScoped<IAgentRunStore>(provider => provider.GetRequiredService<AiAgentRunStore>());
        services.TryAddScoped<IAiOperationBudgetStore, AiOperationBudgetStore>();
        services.TryAddScoped<IAgentRunExecutionContext, AgentRunExecutionContext>();
        RegisterAgentToolExecution(services, forWorker: true);
        services.AddFullNetMcpClient(configuration);
        services.TryAddScoped<AiAgentRunApprovalGate>();
        services.TryAddScoped<AiAgentApprovalConsumption>();
        services.TryAddScoped<AiAgentApprovalService>();
        services.TryAddScoped<AiAgentDelegationService>();
        services.TryAddSingleton<AiAgentWorkerHeartbeatService>();
        services.TryAddScoped<AiAgentRunCoordinator>();
        services.TryAddSingleton<IAgentModelRunner>(_ => AgentFrameworkRuntime.CreateRunner());
        services.AddHostedService<AiAgentRunHostedProcessor>();
    }

    private static void RegisterAgentToolExecution(IServiceCollection services, bool forWorker)
    {
        services.TryAddScoped<IAgentToolApprovalBindingReader, AgentToolApprovalBindingReader>();
        services.TryAddScoped<AiAgentToolCallAuditWriter>();
        services.TryAddScoped<AiAgentToolCatalogService>();
        services.TryAddScoped<PingToolHandler>();
        services.TryAddScoped<ListModelsToolHandler>();
        services.TryAddScoped<ListChatSessionsToolHandler>();
        services.TryAddScoped<RenameChatSessionToolHandler>();
        services.TryAddScoped<AgentToolRegistryFactory>();
        services.TryAddScoped<IAgentToolRegistrySource, AgentToolRegistrySource>();
        services.TryAddScoped<IAgentApprovalPort, AiAgentApprovalPort>();
        services.TryAddScoped<IAgentToolExecutor, AgentToolExecutor>();
        if (forWorker)
        {
            services.Replace(ServiceDescriptor.Scoped<IToolAuthorizationPort, AiBackgroundToolAuthorizationPort>());
            services.Replace(ServiceDescriptor.Scoped<IToolAuditPort, AiBackgroundToolAuditPort>());
        }
        else
        {
            services.TryAddScoped<IToolAuthorizationPort, AiToolAuthorizationPort>();
            services.TryAddScoped<IToolAuditPort, AiToolAuditPort>();
        }
    }
}
