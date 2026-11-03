using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features;

/// <summary>调用事实按查询条件取界，租户状态按唯一Id批量读取；不使用旧缓存、逐行Port或分页后过滤。</summary>
internal sealed class WorkflowCollaborationListAuthorization(IQueryExecutor query, IDataTransactionState transaction, ICurrentTenant tenant,
    IClock clock, IActiveTenantContextResolver tenants, IOptions<DatabaseOptions> database)
{
    internal const int MaximumCalls = 4096;
    internal async Task<Result<string>> PrepareAsync(string kind, Guid actor, Dictionary<string, object?> parameters, CancellationToken token)
    {
        if (transaction.HasTransaction) throw new InvalidOperationException("List authorization cannot borrow a command transaction.");
        if (actor == Guid.Empty || kind is not ("instance" or "pending" or "history" or "claim" or "recovery")) return Denied();
        var scope = WorkflowManagementScope.Resolve(tenant); var now = clock.UtcNow;
        parameters["ActorUserId"] = actor; parameters["ActorUserText"] = actor.ToString("D"); parameters["QueryKind"] = kind;
        parameters["TenantScopeKey"] = scope.TenantScopeKey; parameters["Now"] = now; parameters["CollaborationTake"] = MaximumCalls + 1;
        foreach (var key in new[] { "DefinitionKey", "DefinitionVersionId", "BusinessType", "StartedById", "StatusKey", "StartedFromUtc", "StartedToUtc",
                     "ResultActionKey", "ArrivedFromUtc", "ArrivedToUtc", "CompletedFromUtc", "CompletedToUtc" }) parameters.TryAdd(key, null);
        var candidates = await query.QueryAsync<WorkflowCollaborationListCandidate>(database.Value.Provider == DatabaseProvider.SqlServer
                ? WorkflowCollaborationListSql.CandidatesSqlServer : WorkflowCollaborationListSql.CandidatesMySql, parameters, token).ConfigureAwait(false);
        if (candidates.Count > MaximumCalls) return Result<string>.Failure(new(WorkflowErrorCodes.CollaborationQueryTooBroad,
            "Use a more specific workflow resource or query to limit collaboration authorization facts.", ErrorType.Validation));
        if (candidates.Count == 0) return Result<string>.Success("[]");
        var grants = new Dictionary<Guid, WorkflowCollaborationGrantFact>();
        foreach (var batch in candidates.Select(row => row.GrantId).Distinct().Chunk(256))
        {
            var rows = await query.QueryAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationListSql.Grants,
                WorkflowSqlParameters.Create(("GrantIds", batch), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            foreach (var row in rows) if (row.TargetTenantScopeKey == scope.TenantScopeKey) grants.TryAdd(row.Id, row);
        }
        var verified = new List<(WorkflowCollaborationListCandidate Row, WorkflowCollaborationCallBinding Binding)>();
        foreach (var row in candidates)
        {
            if (row.TargetTenantScopeKey != scope.TenantScopeKey || row.StatusKey is not ("created" or "active" or "completed") ||
                !WorkflowCollaborationAdmissionProof.TryReadBinding(row, out var binding) || binding!.TargetTenantId != scope.TenantId ||
                !grants.TryGetValue(binding.GrantId, out var grant) || grant.PolicyHash != binding.PolicyHash ||
                grant.SourceTenantScopeKey != binding.SourceTenantScopeKey || grant.TargetTenantScopeKey != binding.TargetTenantScopeKey ||
                grant.SourceTenantId != binding.SourceTenantId || grant.TargetTenantId != binding.TargetTenantId ||
                !WorkflowCollaborationGrantPolicy.TryAuthorize(grant, scope.TenantScopeKey, true, binding.GrantRevision, now, out _, out var accepted) ||
                !accepted!.AllowedUserIds.Contains(actor)) continue;
            verified.Add((row, binding));
        }
        var active = new HashSet<Guid>();
        foreach (var batch in verified.SelectMany(item => new[] { item.Binding.SourceTenantId, item.Binding.TargetTenantId })
                     .OfType<Guid>().Distinct().Chunk(256))
        {
            var found = await tenants.ResolveActiveByIdsAsync(batch, token).ConfigureAwait(false);
            foreach (var id in batch) if (found.TryGetValue(id, out var context) && context.Id == id) active.Add(id);
        }
        var proof = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartArray();
            foreach (var item in verified)
            {
                if (item.Binding.SourceTenantId is { } source && !active.Contains(source) || item.Binding.TargetTenantId is { } target && !active.Contains(target)) continue;
                writer.WriteStartObject(); writer.WriteString("id", item.Row.Id); writer.WriteString("bindingHash", item.Row.BindingHash);
                writer.WriteNumber("grantRevision", item.Binding.GrantRevision); writer.WriteString("policyHash", item.Binding.PolicyHash); writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }).CanonicalJson;
        return Result<string>.Success(proof);
    }
    private static Result<string> Denied() => Result<string>.Failure(new(WorkflowErrorCodes.CollaborationGrantConflict,
        "The current actor cannot read collaboration resources.", ErrorType.Forbidden));
}
