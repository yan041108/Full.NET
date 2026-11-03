using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageConnectorCalls;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Workflow.Features;

/// <summary>资源HTTP入口在业务事务外复核双方活动作用域；写入核心仍须在本地事务内锁定准入。</summary>
internal sealed class WorkflowCollaborationAuthorizationFilter(IQueryExecutor query, ICurrentTenant tenant, IClock clock,
    IActiveTenantContextResolver tenants, IApiResultMapper mapper, IAuthorizationService authorization) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)) return await next(invocation).ConfigureAwait(false);
        var scope = WorkflowManagementScope.Resolve(tenant);
        Guid? instanceId = null;
        if (context.Request.RouteValues.TryGetValue("instanceId", out var instance) && Guid.TryParse(instance?.ToString(), out var root)) instanceId = root;
        else
        {
            foreach (var (key, kind) in new[] { ("todoId", "todo"), ("poolId", "pool"), ("ccId", "cc"), ("taskId", "recovery") })
            {
                if (!context.Request.RouteValues.TryGetValue(key, out var resource) || !Guid.TryParse(resource?.ToString(), out var id)) continue;
                instanceId = await query.QuerySingleOrDefaultAsync<Guid?>(WorkflowCollaborationRelaySql.ResolveResourceRoot,
                    WorkflowSqlParameters.Create(("ResourceId", id), ("Kind", kind), ("TenantScopeKey", scope.TenantScopeKey)), context.RequestAborted).ConfigureAwait(false);
                break;
            }
        }
        if (instanceId is { } current && !(HttpMethods.IsGet(context.Request.Method)
                ? await WorkflowCollaborationRuntimeGate.AllowsReadAsync(current, scope, actor, clock.UtcNow, query, context.RequestAborted, tenants).ConfigureAwait(false)
                : await WorkflowCollaborationRuntimeGate.AllowsAsync(current, scope, actor, clock.UtcNow, query, context.RequestAborted, tenants: tenants).ConfigureAwait(false)))
        {
            var name = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            var safe = name is "workflowListConnectorCalls" or "workflowGetConnectorCall" ||
                name == "workflowReconcileConnectorCall" && invocation.Arguments.OfType<ReconcileWorkflowConnectorRequest>().SingleOrDefault()?.ActionKey is "confirm_no_effect" or "accept_result";
            if (!safe || !(await authorization.AuthorizeAsync(context.User, FullNetPermissionPolicies.For(WorkflowPermissions.ConnectorCallsReconcile)).ConfigureAwait(false)).Succeeded ||
                !await WorkflowCollaborationClosingGate.AllowsAsync(current, scope, query, context.RequestAborted).ConfigureAwait(false))
                return mapper.Map(Result<bool>.Failure(new(WorkflowErrorCodes.CollaborationGrantConflict,
                    "The current collaboration authorization no longer permits this resource.", ErrorType.Forbidden)), context);
        }
        return await next(invocation).ConfigureAwait(false);
    }
}
