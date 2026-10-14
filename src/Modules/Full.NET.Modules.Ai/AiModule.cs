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
    /// <summary>
    /// Ai 模块的唯一稳定标识，固定为 "Ai"。
    /// </summary>
    public string Name => "Ai";

    /// <summary>
    /// Ai 模块依赖 Identity、Tenancy、Files 与 Organization，文件版本仅经权威契约访问。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
        "Files",
        "Organization",
    ];

    /// <summary>注册 Ai 模块的模型配置、连通性测试、聊天会话、配额预算、Agent 运行、MCP 与 AgUI 等服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">宿主配置。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            AiAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, Resources.AiKnowledgeErrorResourceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, Resources.AiPromptErrorResourceSource>());
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
        services.TryAddScoped<Features.ManageKnowledgeBases.AiKnowledgeBaseService>();
        services.TryAddScoped<Features.ManageKnowledgeRoleCandidates.AiKnowledgeRoleCandidateService>();
        services.TryAddScoped<Features.ManageKnowledgeOrganizationCandidates.AiKnowledgeOrganizationCandidateService>();
        services.TryAddScoped<Features.AiKnowledgeReadAccess>();
        services.TryAddScoped<Features.AiKnowledgePermissionGuard>();
        services.TryAddScoped<Features.ManageKnowledgeOrganizations.AiKnowledgeOrganizationGrantService>();
        services.TryAddScoped<Features.ManageKnowledgeRoles.AiKnowledgeRoleGrantService>();
        services.TryAddScoped<Features.ManageKnowledgeMembers.AiKnowledgeMemberService>();
        services.TryAddScoped<Features.ManageKnowledgeDocuments.AiKnowledgeDocumentService>();
        services.TryAddScoped<Features.ManageKnowledgeVersions.AiKnowledgeImportStore>();
        services.TryAddScoped<Features.ManageKnowledgeVersions.AiKnowledgeImportCoordinator>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexCleanupStore>();
        services.TryAddScoped<Features.ManageKnowledgeVersions.AiKnowledgeVersionReader>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexEnqueueService>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexRetryService>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexStatusService>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexReconciliationService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexActivationStore>();
        RegisterKnowledgePdfServices(services, worker: false);
        RegisterKnowledgeDocxServices(services, worker: false);
        services.TryAddScoped<Knowledge.IAiKnowledgeAnswerSourceProof, Knowledge.AiKnowledgeAnswerSourceProof>();
        services.TryAddScoped<Knowledge.AiKnowledgeAnswerHistoryGuard>();
        services.TryAddScoped<Knowledge.AiKnowledgeRetrievalService>();
        services.TryAddScoped<Knowledge.AiKnowledgeAnswerPipeline>();
        services.TryAddScoped<Knowledge.AiKnowledgePaidAnswerService>();
        services.TryAddScoped<Knowledge.AiKnowledgeAnswerStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeAskService>();
        services.TryAddScoped<Features.ManageAnswerFeedback.AiKnowledgeAnswerFeedbackService>();
        services.TryAddScoped<Domain.AiKnowledgeAnswerFeedbackReviewCursor>();
        services.TryAddScoped<Features.ManageAnswerFeedbackReview.AiKnowledgeAnswerFeedbackReviewService>();
        services.TryAddScoped<Features.ManagePromptVersions.AiPromptManagementService>();
        services.TryAddScoped<Features.ManagePromptVersions.AiPromptReader>();
        services.TryAddScoped<AiPromptRunResolver>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTaskStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexSessionService>();
        RegisterKnowledgeFileReferences(services);
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
        services.TryAddScoped<AiAgentRunCompletion>();
        services.AddOptions<AiAgentRuntimeOptions>().BindConfiguration(AiAgentRuntimeOptions.SectionName);
        // API 侧就绪门禁需要读取 StaleThreshold；Worker 侧复用同一单例写入心跳。
        services.TryAddSingleton<AiAgentWorkerHeartbeatService>();
        services.TryAddScoped<AiAgentRunReadiness>();
        services.TryAddScoped<AiAgentRunManagementService>();
        services.TryAddScoped<AiAgentReconciliationService>();
        services.TryAddScoped<AiAgentRunQueryService>();
        services.TryAddScoped<IAgentRunAgUiReader, AiAgentRunAgUiReader>();
        services.TryAddScoped<AgUiStreamService>();
        services.AddFullNetMcpServer(configuration);
        services.AddFullNetMcpClient(configuration, services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(AiDataProcessingPolicy))?.ImplementationInstance as AiDataProcessingPolicy);
        services.TryAddScoped<IMcpToolExposureCatalog, AiMcpToolExposureCatalog>();
        services.TryAddScoped<IMcpSessionSummaryReader, AiMcpSessionSummaryReader>();
        services.TryAddScoped<IMcpRemoteToolCatalog, AiMcpRemoteToolCatalog>();
        services.TryAddScoped<AiMcpRemoteTokenProtector>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                AiJsonSerializerContext.Default));
    }

    /// <inheritdoc />
    public void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
    {
        if (stage == ModulePipelineStage.BeforeExceptionHandler)
            app.UseMiddleware<AiDataObservationMiddleware>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageModelConfigs.Endpoint.Map(endpoints);
        Features.ManageKnowledgeBases.Endpoint.Map(endpoints);
        Features.ManageKnowledgeRoleCandidates.Endpoint.Map(endpoints);
        Features.ManageKnowledgeOrganizationCandidates.Endpoint.Map(endpoints);
        Features.ManageKnowledgeOrganizations.Endpoint.Map(endpoints);
        Features.ManageKnowledgeRoles.Endpoint.Map(endpoints);
        Features.ManageKnowledgeDocuments.Endpoint.Map(endpoints);
        Features.ManageKnowledgeVersions.Endpoint.Map(endpoints);
        Features.ManageKnowledgeIndexing.Endpoint.Map(endpoints);
        Features.TestEmbeddings.Endpoint.Map(endpoints);
        Features.ManageMcpRemoteConnections.Endpoint.Map(endpoints);
        Features.ManageTenantQuotas.Endpoint.Map(endpoints);
        Features.ManageChatSessions.Endpoint.Map(endpoints);
        Features.ManageAnswerFeedback.Endpoint.Map(endpoints);
        Features.ManageAnswerFeedbackReview.Endpoint.Map(endpoints);
        Features.ManagePromptVersions.Endpoint.Map(endpoints);
        Features.ManageAgentTools.Endpoint.Map(endpoints);
        Features.ManageAgentRuns.Endpoint.Map(endpoints);
        Features.ManageAgentRuns.ReconciliationEndpoint.Map(endpoints);
        Features.RouteTasks.Endpoint.Map(endpoints);
        AgUiEndpoint.Map(endpoints);
        endpoints.MapFullNetMcp();
        Features.ManageAgentApprovals.Endpoint.Map(endpoints);
        Features.ManageAgentDelegations.Endpoint.Map(endpoints);
    }

    /// <summary>只在 Worker 注册领取循环；Provider 由 Composition 在 Worker Profile 装配。</summary>
    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        // 后台原会话授权也按目录过滤精确权限，不能依赖 API 入口补齐 AI 权限定义。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationCatalogContributor, AiAuthorizationContributor>());
        RegisterKnowledgeFileReferences(services);
#if FULLNET_AOT_COMPILE
        Persistence.AiBudgetRowReaders.Register();
#endif
        services.AddOptions<Knowledge.AiKnowledgeIndexWorkerOptions>()
            .BindConfiguration(Knowledge.AiKnowledgeIndexWorkerOptions.SectionName)
            .Validate(options => options.BatchSize is >= 1 and <= 32, "BatchSize must be between 1 and 32.")
            .Validate(options => options.PollMilliseconds is >= 250 and <= 60000, "PollMilliseconds must be between 250 and 60000.")
            .ValidateOnStart();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTaskStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexSessionService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexBatchStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexBudgetService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexEmbeddingService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexVectorStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTextManifestStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexActivationStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPersistenceService>();
        RegisterKnowledgePdfServices(services, worker: true);
        RegisterKnowledgeDocxServices(services, worker: true);
        services.TryAddScoped<Knowledge.AiKnowledgeIndexWorkerCoordinator>();
        services.AddHostedService<Knowledge.AiKnowledgeIndexHostedProcessor>();
        services.AddOptions<Knowledge.AiKnowledgeCleanupWorkerOptions>()
            .BindConfiguration(Knowledge.AiKnowledgeCleanupWorkerOptions.SectionName)
            .Validate(options => options.BatchSize is >= 1 and <= 32, "BatchSize must be between 1 and 32.")
            .Validate(options => options.PollMilliseconds is >= 250 and <= 60000, "PollMilliseconds must be between 250 and 60000.")
            .Validate(options => options.RetiredGenerationRetentionHours is >= 24 and <= 8760, "RetiredGenerationRetentionHours must be between 24 and 8760.")
            .ValidateOnStart();
        services.TryAddSingleton<Knowledge.AiKnowledgeCleanupCursor>();
        services.TryAddScoped<Knowledge.AiKnowledgeCleanupWorkerCoordinator>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexCleanupStore>();
        services.TryAddScoped<Features.ManageKnowledgeVersions.AiKnowledgeImportStore>();
        services.TryAddScoped<Features.ManageKnowledgeVersions.AiKnowledgeImportCoordinator>();
        services.AddHostedService<Knowledge.AiKnowledgeCleanupHostedProcessor>();
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
        services.TryAddScoped<AiAgentRunCompletion>();
        services.TryAddScoped<IAgentRunStore>(provider => provider.GetRequiredService<AiAgentRunStore>());
        services.TryAddScoped<Features.ManagePromptVersions.AiPromptReader>();
        services.TryAddScoped<AiPromptRunResolver>();
        services.TryAddScoped<IAiOperationBudgetStore, AiOperationBudgetStore>();
        services.TryAddScoped<IAgentRunExecutionContext, AgentRunExecutionContext>();
        RegisterAgentToolExecution(services, forWorker: true);
        services.AddFullNetMcpClient(configuration, services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(AiDataProcessingPolicy))?.ImplementationInstance as AiDataProcessingPolicy);
        services.TryAddScoped<AiAgentRunApprovalGate>();
        services.TryAddScoped<AiAgentApprovalConsumption>();
        services.TryAddScoped<AiAgentApprovalService>();
        services.TryAddScoped<AiAgentDelegationService>();
        services.TryAddSingleton<AiAgentWorkerHeartbeatService>();
        services.TryAddScoped<AiAgentRunCoordinator>();
        services.TryAddScoped<AiAgentTerminalBudgetRepair>();
        services.TryAddScoped<AiAgentTerminalBudgetRepairProcessor>();
        services.TryAddSingleton<IAgentModelRunner>(_ => AgentFrameworkRuntime.CreateRunner());
        services.AddHostedService<AiAgentRunHostedProcessor>();
    }

    private static void RegisterKnowledgePdfServices(IServiceCollection services, bool worker)
    {
        services.AddOptions<Knowledge.AiKnowledgePdfRuntimeOptions>().BindConfiguration(Knowledge.AiKnowledgePdfRuntimeOptions.SectionName)
            .Validate(options => options.IsValid(worker), "PDF runtime requires trusted absolute paths and an immutable image; Worker files must exist.")
            .ValidateOnStart();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPdfActivationStore>();
        if (!worker) return;
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Knowledge.AiKnowledgePdfRuntimeOptions>>().Value;
            return new Knowledge.AiKnowledgePdfSandboxOptions(options.NodeExecutablePath, options.CliScriptPath, options.ImageId);
        });
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPdfPlanningService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPdfEmbeddingService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPdfManifestStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPdfVectorStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPdfPersistenceService>();
    }

    private static void RegisterKnowledgeDocxServices(IServiceCollection services, bool worker)
    {
        services.AddOptions<Knowledge.AiKnowledgeDocxRuntimeOptions>().BindConfiguration(Knowledge.AiKnowledgeDocxRuntimeOptions.SectionName)
            .Validate(options => options.IsValid(worker), "DOCX runtime requires trusted absolute paths and an immutable image; Worker files must exist.")
            .ValidateOnStart();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocxActivationStore>();
        if (!worker) return;
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Knowledge.AiKnowledgeDocxRuntimeOptions>>().Value;
            return new Knowledge.AiKnowledgeDocxSandboxOptions(options.NodeExecutablePath, options.CliScriptPath, options.ImageId);
        });
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocxPlanningService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocxEmbeddingService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocxManifestStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocxVectorStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocxPersistenceService>();
    }

    private static void RegisterKnowledgeFileReferences(IServiceCollection services)
    {
        // Worker 只注册持久化引用探测，不引入上传、HTTP 或文档管理服务。
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Modules.Files.Contracts.IHostFileReferenceClaimProbe,
            Features.ManageKnowledgeVersions.AiKnowledgeFileReferenceProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Modules.Files.Contracts.ITenantResourceFileOwner,
            Features.ManageKnowledgeVersions.AiKnowledgeFileReferenceProbe>());
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
