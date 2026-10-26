using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

/// <summary>启动选择器只公开当前可信作用域内可启动版本声明的范围，不借用定义管理权限或接受客户端范围。</summary>
internal sealed class WorkflowStarterSelectionQueryService(IQueryExecutor query, ICurrentTenant tenant,
    IHostUserSelectionDirectory hostPages, IHostUserBatchSelectionDirectory hostUsers, ITenantMemberSelectionDirectory members,
    IWorkflowRoleMemberDirectory roles, ITenantFeatureEntitlementPort entitlements)
{
    public async Task<Result<WorkflowStarterSelectionsResponse>> GetAsync(Guid versionId, CancellationToken token = default)
    {
        var read = await ReadAsync(versionId, token).ConfigureAwait(false);
        if (!read.IsSuccess) return Result<WorkflowStarterSelectionsResponse>.Failure(read.Error!);
        var items = read.Value!.Nodes.SelectMany(node => WorkflowDynamicAssigneeDefinitionRules.Sources(node)
            .Where(source => source.StarterSelection is not null).Select(source =>
            {
                var policy = source.StarterSelection!;
                var name = node.Config.TryGetProperty("nodeName", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString()! : node.NodeKey;
                return new WorkflowStarterSelectionDescriptorResponse(policy.SelectionKey, node.NodeKey, name, policy.ModeKey, policy.MaximumUsers, policy.Range.KindKey);
            })).OrderBy(item => item.SelectionKey, StringComparer.Ordinal).ToArray();
        return Result<WorkflowStarterSelectionsResponse>.Success(new(versionId, items));
    }

    public async Task<Result<WorkflowRecipientCandidatePageResponse>> ListAsync(Guid versionId, string selectionKey,
        int page, int pageSize, CancellationToken token = default)
    {
        if (!WorkflowNodeKeyValidator.IsValid(selectionKey) || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Fail<WorkflowRecipientCandidatePageResponse>(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var read = await ReadAsync(versionId, token).ConfigureAwait(false);
        if (!read.IsSuccess) return Result<WorkflowRecipientCandidatePageResponse>.Failure(read.Error!);
        var policy = read.Value!.Nodes.SelectMany(WorkflowDynamicAssigneeDefinitionRules.Sources).Select(source => source.StarterSelection)
            .SingleOrDefault(selection => selection?.SelectionKey == selectionKey);
        if (policy is null) return Fail<WorkflowRecipientCandidatePageResponse>(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, ErrorType.NotFound);
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (policy.Range.KindKey == "role_members")
        {
            var result = await roles.ListActiveMembersOfRolesAsync(policy.Range.RoleIds, page, pageSize, token).ConfigureAwait(false);
            return Result<WorkflowRecipientCandidatePageResponse>.Success(new(result.Items.Select(user => new WorkflowRecipientCandidateResponse(user.Id, user.Username, user.DisplayName)).ToArray(), result.Page, result.PageSize, result.Total));
        }
        if (policy.Range.KindKey == "all")
        {
            if (scope.TenantId is null)
            {
                var result = await hostPages.ListActiveHostUsersAsync(page, pageSize, token).ConfigureAwait(false);
                return Result<WorkflowRecipientCandidatePageResponse>.Success(new(result.Items.Select(user => new WorkflowRecipientCandidateResponse(user.Id, user.Username, user.DisplayName)).ToArray(), result.Page, result.PageSize, result.Total));
            }
            var tenantResult = await members.ListActiveTenantMembersAsync(page, pageSize, token).ConfigureAwait(false);
            return Result<WorkflowRecipientCandidatePageResponse>.Success(new(tenantResult.Items.Select(user => new WorkflowRecipientCandidateResponse(user.Id, user.Username, user.DisplayName)).ToArray(), tenantResult.Page, tenantResult.PageSize, tenantResult.Total));
        }
        IEnumerable<WorkflowRecipientCandidateResponse> choices = scope.TenantId is null
            ? (await hostUsers.FindActiveHostUsersAsync(policy.Range.UserIds, token).ConfigureAwait(false)).Values.Select(user => new WorkflowRecipientCandidateResponse(user.Id, user.Username, user.DisplayName))
            : (await members.FindActiveTenantMembersAsync(policy.Range.UserIds, token).ConfigureAwait(false)).Values.Select(user => new WorkflowRecipientCandidateResponse(user.Id, user.Username, user.DisplayName));
        var available = choices.OrderBy(user => user.Username, StringComparer.Ordinal).ThenBy(user => user.Id).ToArray();
        return Result<WorkflowRecipientCandidatePageResponse>.Success(new(available.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize, available.Length));
    }

    private async Task<Result<WorkflowDefinitionDraft>> ReadAsync(Guid versionId, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope, entitlements, token).ConfigureAwait(false) is { } denial)
            return WorkflowTenantFeatureEntitlementGate.Deny<WorkflowDefinitionDraft>(denial);
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", versionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null) return Fail<WorkflowDefinitionDraft>(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        if (!WorkflowDefinitionLifecycleRules.AllowsNewInstance(asset.DefinitionStatusKey)) return Fail<WorkflowDefinitionDraft>(WorkflowErrorCodes.DefinitionDisabled, ErrorType.Conflict);
        if (!WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(query, asset.FormVersionId, scope, token).ConfigureAwait(false)))
            return Fail<WorkflowDefinitionDraft>(WorkflowErrorCodes.FormExtensionForbidden, ErrorType.Conflict);
        var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        return draft is not null && schema is not null && WorkflowRuntimePlan.TryCreate(draft, schema, out _)
            ? Result<WorkflowDefinitionDraft>.Success(draft) : Fail<WorkflowDefinitionDraft>(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
    }
    private static Result<T> Fail<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "Starter selection candidates are unavailable for this published version.", type));
}

internal sealed record WorkflowStarterSelectionDescriptorResponse(string SelectionKey, string NodeKey, string NodeName, string ModeKey, int MaximumUsers, string RangeKey);
internal sealed record WorkflowStarterSelectionsResponse(Guid DefinitionVersionId, IReadOnlyList<WorkflowStarterSelectionDescriptorResponse> Items);
