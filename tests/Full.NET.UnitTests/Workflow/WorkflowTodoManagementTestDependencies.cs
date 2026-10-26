using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>为待办管理服务测试提供不可 mock 的密封依赖真实实例。</summary>
internal static class WorkflowTodoManagementTestDependencies
{
    /// <summary>普通成员与加签链复用同一收敛器，测试不以另一套票数逻辑替代生产决策。</summary>
    internal static WorkflowApprovalStepResolutionCoordinator CreateStepResolution(IQueryExecutor query, ICommandExecutor command,
        IIdGenerator ids, IOutboxWriter outbox) => new(query, command, CreateGroupDecisions(query, command, ids, outbox),
            new WorkflowHumanActivityActivationWriter(command, ids, new WorkflowNotificationOutboxPublisher(outbox)),
            CreateTransitionExecutor(query, command, ids, outbox), Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            CreateResultCallbacks(query, command, ids));
    internal static WorkflowHumanActivityCompletionCoordinator CreateResultCallbacks(IQueryExecutor query, ICommandExecutor command, IIdGenerator ids) =>
        new(new(query, command, new WorkflowBranchStateCoordinator(query, command), ids), Substitute.For<IOutboxWriter>());
    /// <summary>没有自选声明的普通夹具只需空选择服务；自选行为使用独立真实范围替身验证。</summary>
    internal static WorkflowStarterSelectionPreparation CreateStarterSelectionPreparation() => new(
        Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>());
    /// <summary>办理收敛复用当前夹具的写入边界，不引入额外目录或外部调用。</summary>
    internal static WorkflowTaskDecisionCoordinator CreateTaskDecisions(IQueryExecutor query, ICommandExecutor command,
        IIdGenerator ids, IOutboxWriter outbox) => new(query, command,
            new WorkflowHumanActivityActivationWriter(command, ids, new WorkflowNotificationOutboxPublisher(outbox)),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

    internal static WorkflowGroupApprovalDecisionCoordinator CreateGroupDecisions(IQueryExecutor query, ICommandExecutor command,
        IIdGenerator ids, IOutboxWriter outbox) => new(query, command,
            new WorkflowHumanActivityActivationWriter(command, ids, new WorkflowNotificationOutboxPublisher(outbox)),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
    // 实例串行锁不改变业务事实；失败/重放仍须断言没有其他命令，不能忽略表单或证据写入。
    internal static int CountBusinessCommands(ICommandExecutor command) => command.ReceivedCalls()
        .Count(call => call.GetArguments()[0] is not SqlStatement { Name: "workflow.instance.lock_for_todo_action" });

    internal static WorkflowSignatureCoordinator CreateSignatureCoordinator(IQueryExecutor query, ICommandExecutor command) =>
        new(Substitute.For<IHostFileDescriptorReader>(), Substitute.For<IHostFileContentReader>(),
            Substitute.For<IHostFileReferenceClaimService>(), query, command, Substitute.For<IIdGenerator>(),
            Substitute.For<IClock>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkflowSignatureCoordinator>.Instance,
            HostTenant(), Substitute.For<ITenantResourceFileDescriptorReader>(), Substitute.For<ITenantResourceFileStore>());

    private static ICurrentTenant HostTenant()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        return tenant;
    }

    /// <summary>创建带最小替身依赖的加签服务，供退回与多人审批夹具复用。</summary>
    /// <param name="query">查询执行器替身。</param>
    /// <param name="command">命令执行器替身。</param>
    /// <param name="tenant">当前租户上下文替身。</param>
    /// <returns>可直接注入待办管理服务的加签服务实例。</returns>
    internal static WorkflowTodoCountersignService CreateCountersignService(
        IQueryExecutor query,
        ICommandExecutor command,
        ICurrentTenant tenant)
    {
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var outbox = Substitute.For<IOutboxWriter>();
        return new WorkflowTodoCountersignService(
            query,
            command,
            new PassthroughTransaction(),
            tenant,
            Substitute.For<IClock>(),
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantMemberSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(outbox),
            new WorkflowBranchStateCoordinator(query, command),
            CreateStepResolution(query, command, ids, outbox));
    }

    /// <summary>创建默认回落到发起人语义的办理人协调器，供实例与待办测试复用。</summary>
    /// <param name="hostUsers">可选 Host 用户目录替身。</param>
    /// <param name="tenantUsers">可选 Tenant 用户目录替身。</param>
    /// <returns>可直接注入实例或待办管理服务的协调器。</returns>
    internal static WorkflowHumanActivityAssigneeCoordinator CreateAssigneeCoordinator(
        IHostUserBatchSelectionDirectory? hostUsers = null,
        ITenantMemberSelectionDirectory? tenantUsers = null)
    {
        var roleDirectory = Substitute.For<IWorkflowRoleMemberDirectory>();
        var unitDirectory = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        if (hostUsers is null)
        {
            hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
            hostUsers.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>()).ToDictionary(id => id,
                    id => new HostUserDirectoryEntry(id, "member", "Member")));
        }
        return new WorkflowHumanActivityAssigneeCoordinator(
            new WorkflowAssigneeResolver(
                hostUsers,
                tenantUsers ?? Substitute.For<ITenantMemberSelectionDirectory>(),
                roleDirectory,
                unitDirectory));
    }

    /// <summary>创建并行汇合协调器，供实例与待办测试复用。</summary>
    internal static WorkflowParallelJoinCoordinator CreateParallelJoinCoordinator(
        IQueryExecutor query,
        ICommandExecutor command,
        IIdGenerator? ids = null)
    {
        ids ??= Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        return new WorkflowParallelJoinCoordinator(command, query, ids);
    }

    /// <summary>创建审批迁移执行器，供实例与待办测试复用。</summary>
    internal static WorkflowRuntimeTransitionExecutor CreateTransitionExecutor(
        IQueryExecutor query,
        ICommandExecutor command,
        IIdGenerator ids,
        IOutboxWriter outbox)
    {
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        var notificationPublisher = new WorkflowNotificationOutboxPublisher(outbox);
        var automaticTransitionWriter = new WorkflowAutomaticTransitionWriter(command, ids, ccWriter);
        var approvalActivationWriter = new WorkflowHumanActivityActivationWriter(command, ids, notificationPublisher);
        return new WorkflowRuntimeTransitionExecutor(
            automaticTransitionWriter,
            approvalActivationWriter,
            CreateParallelJoinCoordinator(query, command, ids),
            query,
            new WorkflowActivityScheduler(command, ids),
            new WorkflowBranchStateCoordinator(query, command),
            new Full.NET.Modules.Workflow.Execution.Connectors.WorkflowConnectorCatalog([]), CreateResultCallbacks(query, command, ids),
            new WorkflowBusinessCommandActivationWriter(query, command, ids, new WorkflowBusinessActionCatalog([]), new WorkflowBusinessCommandPublisher(outbox)),
            new WorkflowSubprocessActivationWriter(query, command, ids));
    }

    /// <summary>创建默认放行的发布期办理人校验器，供定义发布测试复用。</summary>
    /// <returns>所有来源均返回有效的校验器。</returns>
    internal static WorkflowAssigneePublishValidator CreateAssigneePublishValidator()
    {
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        var tenantUsers = Substitute.For<ITenantMemberSelectionDirectory>();
        var roleDirectory = Substitute.For<IWorkflowRoleMemberDirectory>();
        var unitDirectory = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        hostUsers.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(userId => userId, userId => new HostUserDirectoryEntry(userId, "user", "User")));
        tenantUsers.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(userId => userId, userId => new TenantUserDirectoryEntry(userId, "user", "User")));
        roleDirectory.FindActiveRolesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(roleId => roleId, roleId => new WorkflowRoleDirectoryEntry(roleId, "role", "Role")));
        roleDirectory.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(roleId => roleId, _ => (IReadOnlyList<Guid>)[Guid.CreateVersion7()]));
        unitDirectory.FindActiveUnitsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(unitId => unitId, unitId => new WorkflowOrganizationUnitDirectoryEntry(unitId, "unit", "Unit")));
        unitDirectory.ResolveLeaderSourceAsync(Arg.Any<Guid>(), Arg.Any<WorkflowOrganizationLeaderSource>(), Arg.Any<CancellationToken>())
            .Returns(call => new WorkflowOrganizationLeaderResolution(true,
                [new(call.ArgAt<WorkflowOrganizationLeaderSource>(1).UnitId ?? Guid.CreateVersion7(), Guid.CreateVersion7(), 1)]));
        return new WorkflowAssigneePublishValidator(hostUsers, tenantUsers, roleDirectory, unitDirectory);
    }

    /// <summary>创建默认放行的附件协调器，供实例与待办测试复用。</summary>
    internal static WorkflowFormAttachmentCoordinator CreateAttachmentCoordinator()
    {
        var claimService = Substitute.For<IHostFileReferenceClaimService>();
        claimService.ClaimAsync(Arg.Any<HostFileReferenceClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<HostFileReferenceClaimResult>.Success(
                new HostFileReferenceClaimResult(
                    Guid.CreateVersion7(),
                    HostFileReferenceClaimStates.Active,
                    new HostFileReference(Guid.CreateVersion7(), 1, null))));
        claimService.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<HostFileReferenceClaimResult>.Success(
                new HostFileReferenceClaimResult(
                    Guid.CreateVersion7(),
                    HostFileReferenceClaimStates.Active,
                    new HostFileReference(Guid.CreateVersion7(), 1, null))));
        claimService.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var descriptorReader = Substitute.For<IHostFileDescriptorReader>();
        descriptorReader.GetReadyDescriptorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HostFileDescriptor(
                Guid.CreateVersion7(),
                "evidence.pdf",
                "application/pdf",
                1024,
                null,
                Guid.CreateVersion7()));

        return new WorkflowFormAttachmentCoordinator(
            claimService,
            descriptorReader,
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkflowFormAttachmentCoordinator>.Instance,
            HostTenant(), Substitute.For<ITenantResourceFileDescriptorReader>());
    }

    private sealed class PassthroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
