using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
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

    private sealed class PassthroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
