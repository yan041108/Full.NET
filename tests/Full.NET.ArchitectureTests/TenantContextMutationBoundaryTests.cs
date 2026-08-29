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
        "src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.CodeGeneration/Retention/CodeGenerationCheckpointRetentionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Files/Cleanup/DeletedHostFileBlobCleanupHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReconciliationHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReferenceClaimReconciliationHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs",
        "src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs",
        "src/Modules/Full.NET.Modules.Jobs/Execution/JobWorkerHeartbeatService.cs",
        "src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/Endpoint.cs",
        "src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/HostUserManagementReferenceService.cs",
        "src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/HostUserManagementTenantScope.cs",
        "src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/DiagnosticPolicyStore.cs",
        "src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs",
        "src/Modules/Full.NET.Modules.Tenancy/TenantResolutionMiddleware.cs",
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
