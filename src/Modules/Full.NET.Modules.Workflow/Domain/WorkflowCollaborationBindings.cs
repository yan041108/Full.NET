using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageCollaborationGrants;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布时批量验证当前源定义、固定表单及已接受政策；运行时仍逐操作复核权威授权。</summary>
internal static class WorkflowCollaborationBindings
{
    internal static async Task<Result<bool>> ValidateAsync(WorkflowDefinitionDraft draft, Guid definitionId, Guid formVersionId,
        WorkflowFormSchema schema, WorkflowManagementScope scope, IQueryExecutor query, DateTimeOffset now, CancellationToken token)
    {
        if (draft.Nodes is null || draft.Nodes.Count > WorkflowDefinitionCompiler.MaximumNodes || draft.Nodes.Any(node => node is null)) return Invalid();
        var policies = new List<WorkflowCollaborationCallPolicy>();
        foreach (var node in draft.Nodes.Where(node => node.NodeTypeKey == WorkflowCollaborationCallPolicy.NodeType))
        {
            if (!WorkflowCollaborationCallPolicy.TryRead(node.Config, out var policy)) return Invalid();
            policies.Add(policy!);
        }
        if (policies.Count == 0) return Result<bool>.Success(true);
        var ids = policies.Select(policy => policy.GrantId).Distinct().ToArray();
        var grants = await query.QueryAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.SourceBindings,
            WorkflowSqlParameters.Create(("GrantIds", ids), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (grants.Count != ids.Length || grants.Select(grant => grant.Id).Distinct().Count() != grants.Count) return Invalid();
        var byId = grants.ToDictionary(grant => grant.Id);
        foreach (var policy in policies)
            if (!byId.TryGetValue(policy.GrantId, out var grant) ||
                !Matches(grant, policy, definitionId, formVersionId, schema, scope.TenantScopeKey, now)) return Invalid();
        return Result<bool>.Success(true);
    }

    internal static bool Matches(WorkflowCollaborationGrantFact grant, WorkflowCollaborationCallPolicy policy, Guid definitionId,
        Guid formVersionId, WorkflowFormSchema schema, string trustedScope, DateTimeOffset now)
    {
        if (grant.Id != policy.GrantId || grant.PolicyHash != policy.PolicyHash ||
            !WorkflowCollaborationGrantPolicy.TryAuthorize(grant, trustedScope, false, policy.GrantRevision, now, out var offer, out var acceptance) ||
            offer!.SourceDefinitionId != definitionId || offer.SourceFormVersionId != formVersionId || acceptance!.MaximumActivities < 2) return false;
        WorkflowCollaborationTargetSummary? summary;
        try { summary = JsonSerializer.Deserialize(grant.TargetSummaryJson!, WorkflowJsonSerializerContext.Default.WorkflowCollaborationTargetSummary); }
        catch (JsonException) { return false; }
        var fields = schema.Sections.SelectMany(section => section.Fields).ToArray();
        if (summary?.InputFields is null || summary.OutputFields is null || summary.DefinitionVersionId != acceptance.TargetDefinitionVersionId ||
            fields.Select(field => field.FieldKey).Distinct(StringComparer.Ordinal).Count() != fields.Length) return false;
        var byKey = fields.ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        return acceptance.InputFields.All(pair => byKey.TryGetValue(pair.Key, out var field) &&
                WorkflowCollaborationValueProjection.Supports(field, true) && summary.InputFields.TryGetValue(pair.Value, out var type) && type == field.FieldTypeKey) &&
            acceptance.OutputFields.All(pair => byKey.TryGetValue(pair.Value, out var field) &&
                WorkflowCollaborationValueProjection.Supports(field, true) && summary.OutputFields.TryGetValue(pair.Key, out var type) && type == field.FieldTypeKey);
    }

    private static Result<bool> Invalid() => Result<bool>.Failure(new(WorkflowErrorCodes.CollaborationGrantInvalid,
        "The fixed collaboration authorization cannot be bound to this source definition and form.", ErrorType.Validation));
}
