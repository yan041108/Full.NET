using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowCollaborationFilePreparationProcessor
{
    private static async Task<bool> FreezeAsync(IServiceProvider services, PreparationState prepared, string projection, CancellationToken token)
    {
        var command = services.GetRequiredService<ICommandExecutor>(); var query = services.GetRequiredService<IQueryExecutor>();
        var clock = services.GetRequiredService<IClock>(); var transaction = services.GetRequiredService<ICommandTransaction>();
        var saved = await transaction.ExecuteResultAsync(async inner =>
        {
            if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    P(("Id", prepared.Root.Id), ("TenantScopeKey", prepared.Call.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Invalid();
            var current = await ReadAsync(services, prepared.Call.Id, prepared.Call.TenantScopeKey, inner).ConfigureAwait(false);
            if (current is null || current.Call.StatusKey != "preparing" || current.Root.StatusKey != "active" || current.Call.Revision != prepared.Call.Revision ||
                current.Call.BindingHash != prepared.Call.BindingHash || !await IsRetainedAsync(query, current, inner).ConfigureAwait(false)) return Result<bool>.Success(false);
            var parameters = P(("Id", current.Call.Id), ("InstanceId", current.Root.Id), ("TenantScopeKey", current.Call.TenantScopeKey),
                ("TargetTenantScopeKey", current.Binding.TargetTenantScopeKey), ("GrantId", current.Binding.GrantId), ("GrantRevision", current.Binding.GrantRevision),
                ("PolicyHash", current.Binding.PolicyHash), ("Now", clock.UtcNow));
            if (await command.ExecuteAsync(WorkflowCollaborationControlSql.LockSourceGrant, parameters, inner).ConfigureAwait(false) != 1 ||
                !WorkflowCollaborationGrantPolicy.TryAuthorize(current.Grant, current.Call.TenantScopeKey, false, current.Binding.GrantRevision, clock.UtcNow, out _, out _)) return Result<bool>.Success(false);
            var binding = current.Binding with { ProjectionHash = WorkflowCollaborationCallPolicy.Hash(projection) };
            var json = JsonSerializer.Serialize(binding, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding); var hash = WorkflowCollaborationCallPolicy.Hash(json);
            parameters["Revision"] = current.Call.Revision; parameters["OldBindingHash"] = current.Call.BindingHash;
            parameters["BindingJson"] = json; parameters["BindingHash"] = hash; parameters["ProjectionJson"] = projection; parameters["ProjectionHash"] = binding.ProjectionHash;
            if (await command.ExecuteAsync(WorkflowCollaborationFileSql.Freeze, parameters, inner).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowCollaborationRelaySql.InsertAdmission, parameters, inner).ConfigureAwait(false) != 1) return Invalid();
            await services.GetRequiredService<IOutboxWriter>().AddAsync(WorkflowCollaborationIntegrationEventTypes.Requested, 1,
                new WorkflowCollaborationRequestedIntegrationEvent(current.Call.Id, current.Call.DeliveryId, hash),
                IntegrationEventMetadata.Create(current.Root.Id.ToString("D"), "fullnet.workflow"), inner).ConfigureAwait(false);
            await AuditPreparationAsync(services, current, "files_ready", null, inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }

    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
