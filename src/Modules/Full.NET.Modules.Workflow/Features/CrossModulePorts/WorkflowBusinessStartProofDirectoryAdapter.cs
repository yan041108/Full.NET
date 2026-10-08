using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Features.CrossModulePorts;

/// <summary>只读核对实例和原启动回执，不调用流程启动器，不接受调用方租户。</summary>
internal sealed class WorkflowBusinessStartProofDirectoryAdapter(IQueryExecutor queries, ICurrentTenant tenant)
    : IWorkflowBusinessStartProofDirectory
{
    public async Task<WorkflowBusinessInstanceSnapshot?> FindAsync(WorkflowBusinessStartProofRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenant.IsHost || !tenant.IsAvailable || tenant.Id is not { } tenantId || tenantId == Guid.Empty ||
            request.InstanceId == Guid.Empty || string.IsNullOrWhiteSpace(request.BusinessType) || request.BusinessType.Length > 100 ||
            string.IsNullOrWhiteSpace(request.BusinessId) || request.BusinessId.Length > 200 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 ||
            string.IsNullOrWhiteSpace(request.InitialValuesJson) || request.InitialValuesJson.Length > 16384)
            return null;
        string initialValues;
        try
        {
            using var document = JsonDocument.Parse(request.InitialValuesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            initialValues = document.RootElement.GetRawText();
        }
        catch (JsonException) { return null; }
        var scope = WorkflowManagementScope.Resolve(tenant);
        var instance = await queries.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", request.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        if (instance is null || instance.Id != request.InstanceId || instance.TenantId != tenantId ||
            instance.ScopeKey != scope.ScopeKey || instance.TenantScopeKey != scope.TenantScopeKey ||
            instance.BusinessType != request.BusinessType.Trim() || instance.BusinessId != request.BusinessId.Trim() ||
            instance.DefinitionVersionId == Guid.Empty || instance.StartedById == Guid.Empty)
            return null;
        var receipt = await queries.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("IdempotencyKey", request.IdempotencyKey.Trim())), cancellationToken).ConfigureAwait(false);
        // 哈希形状与实际启动路径一致；版本幂等键存在但参数不同仍不能成为业务恢复证据。
        var original = $"{instance.DefinitionVersionId:D}\n{request.BusinessType.Trim()}\n{request.BusinessId.Trim()}\n{initialValues}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original)));
        if (receipt is null || receipt.ActionKey != "start" || receipt.ActorUserId != instance.StartedById ||
            receipt.IdempotencyKey != request.IdempotencyKey.Trim() || receipt.RequestHash != hash)
            return null;
        return new(instance.Id, tenantId, instance.DefinitionVersionId, instance.BusinessType, instance.BusinessId,
            instance.BusinessTitle, instance.StatusKey, instance.StartedById, instance.StartedAtUtc,
            instance.StatusKey == "cancelled" ? instance.CancelledAtUtc : instance.CompletedAtUtc);
    }
}
