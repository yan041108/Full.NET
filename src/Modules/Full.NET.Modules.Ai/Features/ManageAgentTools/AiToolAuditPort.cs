using System.Diagnostics;
using System.Security.Claims;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>审计使用独立数据作用域，拒绝或并行请求不与 Handler 复用连接。</summary>
internal sealed class AiToolAuditPort(IServiceScopeFactory scopes, IHttpContextAccessor http, ICurrentTenant tenant) : IToolAuditPort
{
    public ValueTask BeginAsync(ToolInvocation invocation, string permissionCode, string statusKey, string? errorCode, CancellationToken cancellationToken) =>
        WriteAsync((writer, actor, token) => writer.BeginOperationAsync(actor, invocation, permissionCode, statusKey, errorCode,
            Activity.Current?.TraceId.ToString(), token), cancellationToken);

    public ValueTask CompleteAsync(Guid operationId, string statusKey, string? errorCode, int outputBytes, int durationMs, CancellationToken cancellationToken) =>
        WriteAsync((writer, actor, token) => writer.CompleteOperationAsync(actor, operationId, statusKey, errorCode, outputBytes, durationMs, token), cancellationToken);

    private async ValueTask WriteAsync(Func<AiAgentToolCallAuditWriter, Guid, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var actor)
            || (!tenant.IsHost && !tenant.IsAvailable)) throw new InvalidOperationException("A trusted audit actor is required.");
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        try
        {
            if (tenant.Id is { } id) context.SetTenant(new TenantContext(id, tenant.Identifier!, tenant.Name!));
            else context.SetHost();
            await write(scope.ServiceProvider.GetRequiredService<AiAgentToolCallAuditWriter>(), actor, cancellationToken).ConfigureAwait(false);
        }
        finally { context.Clear(); }
    }
}
