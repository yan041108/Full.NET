using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowCollaborationFilePreparationProcessor
{
    private async Task<bool> CloseAsync(IServiceProvider services, PreparationState prepared, CancellationToken token)
    {
        var command = services.GetRequiredService<ICommandExecutor>(); var query = services.GetRequiredService<IQueryExecutor>();
        var transaction = services.GetRequiredService<ICommandTransaction>(); var now = services.GetRequiredService<IClock>().UtcNow;
        var saved = await transaction.ExecuteResultAsync(async inner =>
        {
            if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    P(("Id", prepared.Root.Id), ("TenantScopeKey", prepared.Call.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Invalid();
            var current = await ReadAsync(services, prepared.Call.Id, prepared.Call.TenantScopeKey, inner).ConfigureAwait(false);
            if (current?.Call.StatusKey != "preparing") return Result<bool>.Success(false);
            var waiting = await IsRetainedAsync(query, current, inner).ConfigureAwait(false);
            var parameters = P(("Id", current.Call.Id), ("InstanceId", current.Root.Id), ("TenantScopeKey", current.Call.TenantScopeKey),
                ("StatusKey", waiting ? "denied" : "cancelled"), ("Revision", current.Call.Revision), ("BindingHash", current.Call.BindingHash),
                ("UnusedActivities", current.Call.ReservedActivities), ("Now", now));
            // 没有准入证明才可退回整片额度；已经发布的调用必须走目标真实关闭确认。
            if (await command.ExecuteAsync(WorkflowCollaborationFileSql.ClosePreparing, parameters, inner).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowCollaborationControlSql.ReleaseBudget, parameters, inner).ConfigureAwait(false) != 1) return Invalid();
            if (waiting && current.Root.StatusKey == "active" && await command.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,
                    P(("Id", current.Root.Id), ("TenantScopeKey", current.Call.TenantScopeKey), ("Revision", current.Root.Revision)), inner).ConfigureAwait(false) != 1) return Invalid();
            await AuditPreparationAsync(services, current, "preparation_closed", waiting ? "workflow.collaboration.preparation_closed" : null, inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        if (saved.IsSuccess && saved.Value) await ReleaseBothAsync(prepared.Binding, token).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }

    private static async Task AuditPreparationAsync(IServiceProvider services, PreparationState value, string phase, string? error, CancellationToken token)
    {
        var command = services.GetRequiredService<ICommandExecutor>(); var now = services.GetRequiredService<IClock>().UtcNow;
        var detail = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("nodeTypeKey", "process.collaboration"); writer.WriteString("phaseKey", phase);
            writer.WriteString("bindingHash", value.Call.BindingHash); if (error is not null) writer.WriteString("errorCode", error); writer.WriteEndObject();
        }).CanonicalJson;
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit, P(("Id", services.GetRequiredService<IIdGenerator>().NewId()),
            ("TenantId", value.Root.TenantId), ("ScopeKey", value.Root.ScopeKey), ("InstanceId", value.Root.Id), ("OperationKey", "activity.execute"),
            ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", value.Call.Id), ("OutcomeKey", error is null ? "succeeded" : "failed"),
            ("DetailJson", detail), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
    }
}
