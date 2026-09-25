namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class TenantContextMutationBoundaryTests
{
    private static readonly string[] ConcreteAccessorAllowlist =
    [
        "src/BuildingBlocks/Full.NET.Abstractions/Tenancy/CurrentTenantAccessor.cs",
        "src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs",
    ];

    private static readonly string[] ContextWriterAllowlist =
    [
        "src/BuildingBlocks/Full.NET.Abstractions/Tenancy/CurrentTenantAccessor.cs",
        "src/BuildingBlocks/Full.NET.Abstractions/Tenancy/ICurrentTenantContextWriter.cs",
        "src/BuildingBlocks/Full.NET.Modularity/Messaging/IntegrationEventConsumerDispatcher.cs",
        "src/Hosts/Full.NET.Host.Migrator/Program.cs",
        "src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs",
        "src/Hosts/Full.NET.Host.Worker/OutboxRetentionProcessor.cs",
        "src/Hosts/Full.NET.Host.Worker/Program.cs",
        // 工具审计仅复制可信请求作用域，不从工具参数获取租户，不派发业务操作。
        "src/Modules/Full.NET.Modules.Ai/Features/ManageAgentTools/AiBackgroundToolAuditPort.cs",
        "src/Modules/Full.NET.Modules.Ai/Features/ManageAgentTools/AiToolAuditPort.cs",
        // 领取运行后按持久化记录恢复可信租户作用域，用于会话重验与预算结算。
        "src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunCoordinator.cs",
        // Worker 心跳写入 Global 作用域表，固定 Host 上下文，不从运行参数推断租户。
        "src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentWorkerHeartbeatService.cs",
        // 独立清理作用域只接收已授权请求捕获的租户，结束时清除上下文，不用于新模型派发。
        "src/Modules/Full.NET.Modules.Ai/Streaming/AiChatCleanupScope.cs",
        "src/Modules/Full.NET.Modules.Ai/Streaming/AiChatGenerationLeaseMonitor.cs",
        "src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.CodeGeneration/Retention/CodeGenerationCheckpointRetentionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.DataApproval/Execution/DataApprovalRequestApplicationRecoveryBatchProcessor.cs",
        "src/Modules/Full.NET.Modules.DataApproval/Execution/DataApprovalRequestRecoveryBatchProcessor.cs",
        "src/Modules/Full.NET.Modules.Document/Configuration/DocumentVersionRetentionSettingsBootstrap.cs",
        "src/Modules/Full.NET.Modules.Document/PreviewTasks/DocumentPreviewTaskHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Document/Retention/DocumentVersionRetentionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Files/Cleanup/DeletedHostFileBlobCleanupHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReconciliationHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReferenceClaimReconciliationHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Files/Reconciliation/PendingTenantResourceFileReconciliationRunner.cs",
        "src/Modules/Full.NET.Modules.Identity/Features/AcceptTenantInvitation/AcceptTenantInvitationService.cs",
        "src/Modules/Full.NET.Modules.Identity/Features/AcceptTenantInvitation/IdentityTenantInvitationScope.cs",
        "src/Modules/Full.NET.Modules.Identity/Features/ChangeSessionContext/IdentitySessionContextService.cs",
        // 单次请求内临时 Host 作用域，执行 HostOnly SQL 后恢复租户上下文。
        "src/Modules/Full.NET.Modules.Identity/Features/IdentityHostExecutionScope.cs",
        "src/Modules/Full.NET.Modules.Identity/Features/ManageTenantMembers/TenantMemberProvisionService.cs",
        "src/Modules/Full.NET.Modules.Identity/Features/SelfServiceProfile/SelfServiceProfileMediaService.cs",
        "src/Modules/Full.NET.Modules.Identity/Features/SelfServiceProfile/SelfServiceProfileService.cs",
        "src/Modules/Full.NET.Modules.Identity/Middleware/IdentityOidcHostContextMiddleware.cs",
        "src/Modules/Full.NET.Modules.Identity/Oidc/IdentityOidcAccessSessionValidator.cs",
        // OIDC 登出在 Host 作用域撤销跨租户会话和授权记录，然后恢复原租户。
        "src/Modules/Full.NET.Modules.Identity/Oidc/IdentityOidcAuthorizationService.cs",
        "src/Modules/Full.NET.Modules.Identity/Oidc/IdentityOidcClientRegistrar.cs",
        "src/Modules/Full.NET.Modules.Identity/Retention/AuthenticationEventRetentionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Identity/Retention/IdentityOidcRetentionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Identity/Security/FullNetJwtBearerEvents.cs",
        // 种子数据在 Host 作用域写入成员关系，租户来自已授权种子上下文而非请求参数。
        "src/Modules/Full.NET.Modules.Identity/Seeding/BootstrapAdminTenantMembershipSeedContributor.cs",
        "src/Modules/Full.NET.Modules.ImportExport/ImportTasks/ImportExportTaskRunner.cs",
        "src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs",
        "src/Modules/Full.NET.Modules.Jobs/Execution/JobWorkerHeartbeatService.cs",
        "src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobExecutions/HostJobTriggerService.cs",
        "src/Modules/Full.NET.Modules.Jobs/Middleware/HostJobsHostContextMiddleware.cs",
        "src/Modules/Full.NET.Modules.Jobs/Middleware/HostJobsHostContextScope.cs",
        "src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/Endpoint.cs",
        "src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/HostUserManagementReferenceService.cs",
        "src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/HostUserManagementTenantScope.cs",
        "src/Modules/Full.NET.Modules.Reporting/Features/ManageExportTasks/ReportingExportTaskRunner.cs",
        "src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/DiagnosticPolicyStore.cs",
        "src/Modules/Full.NET.Modules.Tenancy/Features/TenancyHostExecutionScope.cs",
        "src/Modules/Full.NET.Modules.Tenancy/Features/TenantBranding/TenantBrandingMediaService.cs",
        "src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs",
        "src/Modules/Full.NET.Modules.Tenancy/TenantResolutionMiddleware.cs",
        // Webhook 后台批处理在独立作用域中固定 Host 上下文，结束后清除，避免沿用请求租户。
        "src/Modules/Full.NET.Modules.Webhooks/Delivery/WebhookDeliveryHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Workflow/Execution/WorkflowTodoTimeoutProcessor.cs",
    ];

    [TestMethod]
    public void Production_code_does_not_depend_on_concrete_current_tenant_accessor()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var offenders = EnumerateProductionSources(root)
            .Where(item => item.Content.Contains(
                "CurrentTenantAccessor",
                StringComparison.Ordinal))
            .Select(item => item.Path)
            .Where(path => !ConcreteAccessorAllowlist.Contains(path, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Tenant_context_write_capability_is_limited_to_reviewed_infrastructure_boundaries()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var consumers = EnumerateProductionSources(root)
            .Where(item => item.Content.Contains(
                "ICurrentTenantContextWriter",
                StringComparison.Ordinal))
            .Select(item => item.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(ContextWriterAllowlist, consumers);
    }

    private static IEnumerable<(string Path, string Content)> EnumerateProductionSources(
        string root)
    {
        var sourceRoot = Path.Combine(root, "src");
        return Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOutput(path))
            .Select(path => (
                Path: Path.GetRelativePath(root, path).Replace('\\', '/'),
                Content: File.ReadAllText(path)));
    }

    private static bool IsGeneratedOutput(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }
}
