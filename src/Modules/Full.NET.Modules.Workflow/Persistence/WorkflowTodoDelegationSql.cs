using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>代理权只来自同作用域不可变交接入边，是否仍生效由目标待办和实例状态裁定。</summary>
internal static class WorkflowTodoDelegationSql
{
    public static readonly SqlStatement FindForOrigin = new("workflow.todo_delegation.find_for_origin", """
        SELECT handoff.FromTodoId AS OriginTodoId, handoff.ToTodoId AS ProxyTodoId,
            handoff.FromAssigneeUserId AS OriginalAssigneeUserId, handoff.ToAssigneeUserId AS ProxyUserId, handoff.CreatedAtUtc
        FROM fn_workflow_todo_transfer AS handoff
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=handoff.InstanceId
        WHERE handoff.FromTodoId=@TodoId AND handoff.AuthorizationKey='workflow.todos.delegate'
          AND handoff.TenantScopeKey=@TenantScopeKey AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindForProxy = new("workflow.todo_delegation.find_for_proxy", """
        SELECT handoff.FromTodoId AS OriginTodoId, handoff.ToTodoId AS ProxyTodoId,
            handoff.FromAssigneeUserId AS OriginalAssigneeUserId, handoff.ToAssigneeUserId AS ProxyUserId, handoff.CreatedAtUtc
        FROM fn_workflow_todo_transfer AS handoff
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=handoff.InstanceId
        WHERE handoff.ToTodoId=@TodoId AND handoff.AuthorizationKey='workflow.todos.delegate'
          AND handoff.TenantScopeKey=@TenantScopeKey AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
}

/// <summary>委托人与代理人来自已提交事实，不接受请求体替代原办理人。</summary>
internal sealed record WorkflowTodoDelegationRecord(Guid OriginTodoId, Guid ProxyTodoId,
    Guid OriginalAssigneeUserId, Guid ProxyUserId, DateTimeOffset CreatedAtUtc);
