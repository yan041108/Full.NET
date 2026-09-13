using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>Worker 审计使用运行绑定主体，不依赖 HttpContext。</summary>
internal sealed class AiBackgroundToolAuditPort(
    IServiceScopeFactory scopes,
    IAgentRunExecutionContext executionContext,
    ICurrentTenant tenant) : IToolAuditPort
{
    public ValueTask BeginAsync(ToolInvocation invocation, string permissionCode, string statusKey, string? errorCode, CancellationToken cancellationToken) =>
        WriteAsync((writer, actor, token) => writer.BeginOperationAsync(actor, invocation, permissionCode, statusKey, errorCode,
            null, token), cancellationToken);

    public ValueTask CompleteAsync(Guid operationId, string statusKey, string? errorCode, int outputBytes, int durationMs, CancellationToken cancellationToken) =>
        WriteAsync((writer, actor, token) => writer.CompleteOperationAsync(actor, operationId, statusKey, errorCode, outputBytes, durationMs, token), cancellationToken);

    private async ValueTask WriteAsync(Func<AiAgentToolCallAuditWriter, Guid, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        var binding = executionContext.Binding
            ?? throw new InvalidOperationException("Agent run execution binding is required for background tool audit.");
        if (!tenant.IsHost && !tenant.IsAvailable)
        {
            throw new InvalidOperationException("A trusted audit tenant is required.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        try
        {
            if (tenant.Id is { } id)
            {
                context.SetTenant(new TenantContext(id, tenant.Identifier!, tenant.Name!));
            }
            else
            {
                context.SetHost();
            }

            await write(scope.ServiceProvider.GetRequiredService<AiAgentToolCallAuditWriter>(), binding.UserId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            context.Clear();
        }
    }
}
