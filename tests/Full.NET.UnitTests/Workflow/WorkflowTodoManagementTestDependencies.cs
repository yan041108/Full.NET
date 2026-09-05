using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>为待办管理服务测试提供不可 mock 的密封依赖真实实例。</summary>
internal static class WorkflowTodoManagementTestDependencies
{
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
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        return new WorkflowTodoCountersignService(
            query,
            command,
            new PassthroughTransaction(),
            tenant,
            Substitute.For<IClock>(),
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantUserSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(outbox),
            new WorkflowAutomaticTransitionWriter(command, ids, ccWriter));
    }

    /// <summary>创建默认回落到发起人语义的办理人协调器，供实例与待办测试复用。</summary>
    /// <param name="hostUsers">可选 Host 用户目录替身。</param>
    /// <param name="tenantUsers">可选 Tenant 用户目录替身。</param>
    /// <returns>可直接注入实例或待办管理服务的协调器。</returns>
    internal static WorkflowApprovalAssigneeCoordinator CreateAssigneeCoordinator(
        IHostUserBatchSelectionDirectory? hostUsers = null,
        ITenantUserSelectionDirectory? tenantUsers = null)
    {
        var roleDirectory = Substitute.For<IWorkflowRoleMemberDirectory>();
        var unitDirectory = Substitute.For<IWorkflowUnitLeaderDirectory>();
        return new WorkflowApprovalAssigneeCoordinator(
            new WorkflowAssigneeResolver(
                hostUsers ?? Substitute.For<IHostUserBatchSelectionDirectory>(),
                tenantUsers ?? Substitute.For<ITenantUserSelectionDirectory>(),
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
    internal static WorkflowApprovalTransitionExecutor CreateTransitionExecutor(
        IQueryExecutor query,
        ICommandExecutor command,
        IIdGenerator ids,
        IOutboxWriter outbox,
        WorkflowApprovalAssigneeCoordinator? assigneeCoordinator = null)
    {
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        var notificationPublisher = new WorkflowNotificationOutboxPublisher(outbox);
        var automaticTransitionWriter = new WorkflowAutomaticTransitionWriter(command, ids, ccWriter);
        var approvalActivationWriter = new WorkflowApprovalActivationWriter(command, ids, notificationPublisher);
        return new WorkflowApprovalTransitionExecutor(
            automaticTransitionWriter,
            approvalActivationWriter,
            CreateParallelJoinCoordinator(query, command, ids),
            assigneeCoordinator ?? CreateAssigneeCoordinator(),
            query);
    }

    /// <summary>创建默认放行的发布期办理人校验器，供定义发布测试复用。</summary>
    /// <returns>所有来源均返回有效的校验器。</returns>
    internal static WorkflowAssigneePublishValidator CreateAssigneePublishValidator()
    {
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        var roleDirectory = Substitute.For<IWorkflowRoleMemberDirectory>();
        var unitDirectory = Substitute.For<IWorkflowUnitLeaderDirectory>();
        hostUsers.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(userId => userId, userId => new HostUserDirectoryEntry(userId, "user", "User")));
        tenantUsers.FindActiveTenantUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(userId => userId, userId => new TenantUserDirectoryEntry(userId, "user", "User")));
        roleDirectory.FindActiveRolesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(roleId => roleId, roleId => new WorkflowRoleDirectoryEntry(roleId, "role", "Role")));
        roleDirectory.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(roleId => roleId, _ => (IReadOnlyList<Guid>)[Guid.CreateVersion7()]));
        unitDirectory.FindActiveUnitsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(unitId => unitId, unitId => new WorkflowOrganizationUnitDirectoryEntry(unitId, "unit", "Unit")));
        unitDirectory.FindActiveUnitLeaderUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>())
                .ToDictionary(unitId => unitId, _ => Guid.CreateVersion7()));
        return new WorkflowAssigneePublishValidator(hostUsers, tenantUsers, roleDirectory, unitDirectory);
    }

    private sealed class PassthroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
