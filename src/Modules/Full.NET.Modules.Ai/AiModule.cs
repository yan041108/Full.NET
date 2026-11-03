// Ai 模块宿主入口；注册模型、聊天、知识库、Agent、MCP、AgUI 与相关 API。
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
using Full.NET.AgenticWeb.Mcp;
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
    /// Ai 模块依赖 Identity、Tenancy、Files、Organization 与 Calendar；来源仅经所有者权威契约访问。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
        "Files",
        "Organization",
        "Calendar",
    ];

    /// <summary>注册 Ai 模块的模型配置、连通性测试、聊天会话、配额预算、Agent 运行、MCP 与 AgUI 等服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">宿主配置。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        Persistence.AiLogTimelineSources.Register(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            AiAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, Resources.AiKnowledgeErrorResourceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, Resources.AiKnowledgeEvaluationSetErrorResourceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, Resources.AiKnowledgeEvaluationRunErrorResourceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, Resources.AiPromptErrorResourceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, Resources.AiAgentRunErrorResourceSource>());
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
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalIndexWriter>();
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalRecoveryService>();
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeIndexChangeService>();
        services.TryAddScoped<Features.ManageKnowledgeVersions.AiKnowledgeImportCoordinator>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexCleanupStore>();
        services.TryAddScoped<Features.ManageKnowledgeVersions.AiKnowledgeVersionReader>();
        services.AddOptions<Knowledge.AiKnowledgeIndexRebuildOptions>().BindConfiguration(Knowledge.AiKnowledgeIndexRebuildOptions.SectionName).ValidateOnStart();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexRebuildService>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexEnqueueService>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexRetryService>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexStatusService>();
        services.TryAddScoped<Features.ManageKnowledgeOperations.AiKnowledgeOperationsService>();
        services.TryAddScoped<Features.ManageKnowledgeEvaluationSets.AiKnowledgeEvaluationService>();
        services.TryAddScoped<Features.ManageKnowledgeEvaluationSets.AiKnowledgeEvaluationRunService>();
        services.TryAddScoped<Features.ManageKnowledgeOperations.AiKnowledgeQualityService>();
        services.TryAddScoped<Features.ManageKnowledgeEvaluationSets.AiKnowledgeEvaluationRunAccess>();
        services.TryAddScoped<Knowledge.AiPersonalScheduleSourceReader>();
        services.TryAddScoped<Knowledge.AiPersonalScheduleContinuationGate>();
        services.TryAddScoped<Knowledge.AiKnowledgeEvaluationOperationScope>();
        services.TryAddScoped<Knowledge.AiKnowledgeEvaluationSourceAuthority>();
        services.TryAddScoped<Domain.AiKnowledgeEvaluationCandidateToken>();
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexReconciliationService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexActivationStore>();
        RegisterKnowledgePdfServices(services, worker: false);
        RegisterKnowledgeDocxServices(services, worker: false);
        RegisterKnowledgeDocumentServices(services, worker: false);
        services.TryAddScoped<Knowledge.IAiKnowledgeAnswerSourceProof, Knowledge.AiKnowledgeAnswerSourceProof>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionActivationStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeAnswerHistoryGuard>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionAuthority>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionWorkspaceReader>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionWorkbenchService>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionEditService>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionPreparationQueue>();
        services.TryAddScoped<Features.ManageKnowledgePublications.AiKnowledgePublicationService>();
        services.TryAddScoped<Features.ManageKnowledgePublications.AiKnowledgeDirectoryService>();
        services.TryAddScoped<Knowledge.AiKnowledgeGenerationLeases>();
        services.TryAddScoped<Knowledge.AiKnowledgeRetrievalService>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqPublishedSourceReader>();
        services.TryAddScoped<Features.ManageKnowledgeFaqs.AiKnowledgeFaqDraftService>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqFeedbackPreparation>();
        services.TryAddScoped<Features.ManageKnowledgeFaqs.AiKnowledgeFaqLifecycleAccess>();
        services.TryAddScoped<Features.ManageKnowledgeFaqs.AiKnowledgeFaqIndexService>();
        services.TryAddScoped<Features.ManageKnowledgeFaqs.AiKnowledgeFaqPublicationService>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqClassification>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqNodeReader>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqDraftSourceGuard>();
        services.TryAddScoped<Features.SearchKnowledge.AiKnowledgeSearchService>();
        services.TryAddScoped<Features.SearchKnowledge.AiKnowledgeSearchContextService>();
        services.TryAddScoped<Features.SearchKnowledge.AiKnowledgeSearchRelationsService>();
        services.TryAddScoped<Features.SearchKnowledge.AiKnowledgeSearchRefinementService>();
        services.TryAddScoped<Features.SearchKnowledge.AiKnowledgeSearchComparisonService>();
        services.TryAddScoped<Knowledge.AiKnowledgeAnswerPipeline>();
        services.TryAddScoped<Knowledge.AiKnowledgePaidAnswerService>();
        services.TryAddScoped<Knowledge.AiKnowledgeAnswerStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeAskService>();
        services.TryAddScoped<Features.ManageAnswerFeedback.AiKnowledgeAnswerFeedbackService>();
        services.TryAddScoped<Domain.AiKnowledgeAnswerFeedbackReviewCursor>();
        services.TryAddScoped<Domain.AiFeedbackEvaluationToken>();
        services.TryAddScoped<Features.ManageAnswerFeedbackReview.AiFeedbackEvaluationService>();
        services.TryAddScoped<Features.ManageAnswerFeedbackReview.AiKnowledgeAnswerFeedbackReviewService>();
        services.TryAddScoped<Features.ManagePromptVersions.AiPromptManagementService>();
        services.TryAddScoped<Features.ManagePromptVersions.AiPromptReader>();
        services.TryAddScoped<AiPromptRunResolver>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTaskScope>();
        services.TryAddScoped<Features.AiKnowledgeReadAccess>();
        services.TryAddScoped<Features.AiKnowledgePermissionGuard>();
        services.TryAddScoped<Knowledge.IAiKnowledgeAnswerSourceProof, Knowledge.AiKnowledgeAnswerSourceProof>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqClassification>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqNodeReader>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqDraftSourceGuard>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqIndexInput>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTaskStore>();
        services.TryAddScoped<Diagnostics.AiBackgroundLogCorrelation>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexSessionService>();
        RegisterKnowledgeGrantServices(services);
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeIndexGrantService>();
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
        {
            app.UseMcpOriginAdmission();
            app.UseMiddleware<AiDataObservationMiddleware>();
        }
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageModelConfigs.Endpoint.Map(endpoints);
        Features.ManageKnowledgeBases.Endpoint.Map(endpoints);
        Features.ManageKnowledgePublications.Endpoint.Map(endpoints);
        Features.ManageKnowledgePublications.DirectoryEndpoint.Map(endpoints);
        Features.ManageKnowledgeRoleCandidates.Endpoint.Map(endpoints);
        Features.ManageKnowledgeOrganizationCandidates.Endpoint.Map(endpoints);
        Features.ManageKnowledgeOrganizations.Endpoint.Map(endpoints);
        Features.ManageKnowledgeRoles.Endpoint.Map(endpoints);
        Features.ManageKnowledgeDocuments.Endpoint.Map(endpoints);
        Features.ManageKnowledgeVersions.Endpoint.Map(endpoints);
        Features.ManageKnowledgeCorrections.Endpoint.Map(endpoints);
        Features.ManageKnowledgeFaqs.Endpoint.Map(endpoints);
        Features.ManageKnowledgeOperations.Endpoint.Map(endpoints);
        Features.ManageKnowledgeEvaluationSets.Endpoint.Map(endpoints);
        Features.ManageKnowledgeEvaluationSets.RunEndpoint.Map(endpoints);
        Features.PersonalScheduleAssistant.SourceEndpoint.Map(endpoints);
        Features.ManageKnowledgeOperations.QualityEndpoint.Map(endpoints);
        Features.ManageKnowledgeFaqs.LifecycleEndpoint.Map(endpoints);
        Features.ManageKnowledgeFaqs.TransferEndpoint.Map(endpoints);
        Features.SearchKnowledge.Endpoint.Map(endpoints);
        Features.ManageKnowledgeIndexing.Endpoint.Map(endpoints);
        Features.IncrementalKnowledgeIndexing.Endpoint.Map(endpoints);
        Features.TestEmbeddings.Endpoint.Map(endpoints);
        Features.ManageMcpRemoteConnections.Endpoint.Map(endpoints);
        Features.ManageTenantQuotas.Endpoint.Map(endpoints);
        Features.ManageChatSessions.Endpoint.Map(endpoints);
        Features.ManageAnswerFeedback.Endpoint.Map(endpoints);
        Features.ManageAnswerFeedbackReview.Endpoint.Map(endpoints);
        Features.IncrementalKnowledgeIndexing.AiKnowledgeIndexGrantEndpoint.Map(endpoints);
        Features.ManagePromptVersions.Endpoint.Map(endpoints);
        Features.ManageAgentTools.Endpoint.Map(endpoints);
        Features.ManageAgentRuns.Endpoint.Map(endpoints);
        Features.CompletePersonalSchedules.Endpoint.Map(endpoints);
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
        // FAQ完整业务来源在后台仍通过源模块原会话契约核证，不借用或伪造HttpContext。
        services.TryAddScoped<Knowledge.AiPersonalScheduleSourceReader>();
        RegisterKnowledgeGrantServices(services);
        services.TryAddScoped<Features.ManageKnowledgeIndexing.AiKnowledgeIndexEnqueueService>();
        services.AddHostedService<Knowledge.AiKnowledgeIndexGrantProcessor>();
        services.AddOptions<Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalRecoveryWorkerOptions>()
            .BindConfiguration(Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalRecoveryWorkerOptions.SectionName)
            .Validate(options => options.BatchSize is >= 1 and <= 32, "BatchSize must be between 1 and 32.")
            .Validate(options => options.PollMilliseconds is >= 1000 and <= 60000, "PollMilliseconds must be between 1000 and 60000.")
            .ValidateOnStart();
        services.TryAddSingleton<Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalRecoveryCursor>();
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalRecoveryWorker>();
        services.AddHostedService<Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalRecoveryHostedProcessor>();
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeIncrementalIndexWriter>();
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeSourceConfirmedHandler>();
        services.TryAddScoped<Features.IncrementalKnowledgeIndexing.AiKnowledgeDocumentMetadataUpdatedHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Abstractions.Messaging.IIntegrationEventHandler,
            Features.IncrementalKnowledgeIndexing.AiKnowledgeDocumentMetadataUpdatedHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Abstractions.Messaging.IIntegrationEventHandler,
            Features.IncrementalKnowledgeIndexing.AiKnowledgeSourceConfirmedHandler>());
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
        services.TryAddScoped<Diagnostics.AiBackgroundLogCorrelation>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTaskScope>();
        // 后台完整来源核证只使用任务原会话与现有目录 Port，不装配交互 HTTP 授权栈。
        services.TryAddScoped<Features.AiKnowledgeReadAccess>();
        services.TryAddScoped<Knowledge.IAiKnowledgeAnswerSourceProof, Knowledge.AiKnowledgeAnswerSourceProof>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqClassification>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqNodeReader>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqDraftSourceGuard>();
        services.TryAddScoped<Knowledge.AiKnowledgeFaqIndexInput>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTaskStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexSessionService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexBatchStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionAuthority>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionPreparationService>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionPreparationQueue>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionPreparationCoordinator>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionIndexService>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionBudgetService>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionEmbeddingService>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionVectorStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionPersistenceService>();
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionActivationStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexBudgetService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexEmbeddingService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexVectorStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexTextManifestStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexActivationStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexPersistenceService>();
        RegisterKnowledgePdfServices(services, worker: true);
        RegisterKnowledgeDocxServices(services, worker: true);
        RegisterKnowledgeDocumentServices(services, worker: true);
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
        services.TryAddScoped<Knowledge.AiKnowledgeCorrectionCleanupStore>();
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
        // 工具执行器逐次重建目录；后台必须自行闭合依赖，不能依赖仅在 API 注册的服务。
        // 目录仍经原运行会话的后台授权 Port 过滤，不引入交互 HTTP 权限或 MCP 服务端。
        services.TryAddScoped<IMcpRemoteToolCatalog, AiMcpRemoteToolCatalog>();
        services.TryAddScoped<AiMcpRemoteTokenProtector>();
        services.TryAddScoped<AiAgentRunApprovalGate>();
        services.TryAddScoped<AiAgentApprovalConsumption>();
        services.TryAddScoped<AiAgentApprovalService>();
        services.TryAddScoped<AiAgentDelegationService>();
        services.TryAddSingleton<AiAgentWorkerHeartbeatService>();
        services.TryAddScoped<AiAgentRunCoordinator>();
        services.TryAddScoped<AiAgentTerminalBudgetRepair>();
        services.TryAddScoped<AiAgentCompletedReceiptReconciliation>();
        services.TryAddScoped<AiAgentTerminalBudgetRepairProcessor>();
        services.TryAddSingleton<IAgentModelRunner>(_ => AgentFrameworkRuntime.CreateRunner());
        services.AddHostedService<AiAgentHeartbeatHostedService>();
        services.AddHostedService<AiAgentRunHostedProcessor>();
    }

    private static void RegisterKnowledgeGrantServices(IServiceCollection services)
    {
        services.AddOptions<Knowledge.AiKnowledgeIndexGrantOptions>().BindConfiguration(Knowledge.AiKnowledgeIndexGrantOptions.SectionName)
            .Validate(options => options.BatchSize is >= 1 and <= 16, "BatchSize must be between 1 and 16.")
            .Validate(options => options.PollMilliseconds is >= 1000 and <= 60000, "PollMilliseconds must be between 1000 and 60000.").ValidateOnStart();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexGrantStore>();
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
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocumentActivationStore>();
        if (!worker) return;
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Knowledge.AiKnowledgeDocxRuntimeOptions>>().Value;
            return new Knowledge.AiKnowledgeDocxSandboxOptions(options.NodeExecutablePath, options.CliScriptPath, options.ImageId);
        });
        services.TryAddScoped<Knowledge.AiKnowledgeDocumentPlanningService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocumentEmbeddingService>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocumentManifestStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocumentVectorStore>();
        services.TryAddScoped<Knowledge.AiKnowledgeIndexDocumentPersistenceService>();
    }

    private static void RegisterKnowledgeDocumentServices(IServiceCollection services, bool worker)
    {
        services.AddOptions<Knowledge.AiKnowledgeDocumentRuntimeOptions>().BindConfiguration(Knowledge.AiKnowledgeDocumentRuntimeOptions.SectionName)
            .Validate(options => options.IsValid(worker), "Document runtime requires a bounded resource profile, trusted paths and an immutable image.")
            .ValidateOnStart();
        if (!worker) return;
        services.TryAddSingleton<Knowledge.AiKnowledgeDocumentParseGate>();
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Knowledge.AiKnowledgeDocumentRuntimeOptions>>().Value;
            return new Knowledge.AiKnowledgeDocumentSandboxOptions(options.NodeExecutablePath, options.CliScriptPath, options.ImageId, options.TimeoutMs, options.Enabled);
        });
    }

    private static void RegisterKnowledgeFileReferences(IServiceCollection services)
    {
        var metadataTopic = global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition.Create(
            Features.IncrementalKnowledgeIndexing.AiKnowledgeDocumentMetadataUpdated.TopicCode,
            Features.IncrementalKnowledgeIndexing.AiKnowledgeDocumentMetadataUpdated.EventType, 1,
            global::Full.NET.Messaging.Abstractions.EventDeliveryOwner.LegacyPolling);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition)
            && descriptor.ImplementationInstance is global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition metadata
            && metadata.TopicCode == metadataTopic.TopicCode))
            services.AddSingleton(metadataTopic);
        var sourceTopic = global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition.Create(
            Features.IncrementalKnowledgeIndexing.AiKnowledgeSourceConfirmed.TopicCode,
            Features.IncrementalKnowledgeIndexing.AiKnowledgeSourceConfirmed.EventType, 1,
            global::Full.NET.Messaging.Abstractions.EventDeliveryOwner.LegacyPolling);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition)
            && descriptor.ImplementationInstance is global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition topic
            && topic.TopicCode == sourceTopic.TopicCode))
            services.AddSingleton(sourceTopic);
        // Worker 只注册持久化引用探测，不引入上传、HTTP 或文档管理服务。
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Modules.Files.Contracts.IHostFileReferenceClaimProbe,
            Features.ManageKnowledgeVersions.AiKnowledgeFileReferenceProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Modules.Files.Contracts.ITenantResourceFileOwner,
            Features.ManageKnowledgeVersions.AiKnowledgeFileReferenceProbe>());
    }

    private static void RegisterAgentToolExecution(IServiceCollection services, bool forWorker)
    {
        services.TryAddScoped<AiWorkflowTargetAdmission>();
        services.TryAddScoped<IAgentRunExecutionContext, AgentRunExecutionContext>();
        services.TryAddScoped<Features.CompletePersonalSchedules.AiScheduleCompletionService>();
        services.TryAddScoped<Features.CompletePersonalSchedules.ScheduleCompletionToolHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Abstractions.Messaging.IIntegrationEventHandler,
            Features.CompletePersonalSchedules.AiScheduleCompletionRequestedHandler>());
        var completionTopic = global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition.Create(
            "ai.schedule-completion-requested.v1", Features.CompletePersonalSchedules.AiScheduleCompletionRequested.EventType, 1,
            global::Full.NET.Messaging.Abstractions.EventDeliveryOwner.LegacyPolling);
        if (!services.Any(d => d.ServiceType == typeof(global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition)
            && d.ImplementationInstance is global::Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition t && t.TopicCode == completionTopic.TopicCode))
            services.AddSingleton(completionTopic);
        services.TryAddScoped<IAgentToolApprovalBindingReader, AgentToolApprovalBindingReader>();
        services.TryAddScoped<AiAgentToolCallAuditWriter>();
        services.TryAddScoped<AiAgentToolCatalogService>();
        services.TryAddScoped<PingToolHandler>();
        services.TryAddScoped<ListModelsToolHandler>();
        services.TryAddScoped<ListChatSessionsToolHandler>();
        services.TryAddScoped<GetChatSessionMetadataToolHandler>();
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
