using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Features.ManageScenarios;

/// <summary>管理 DataApproval 静态场景目录与受控工作流绑定。</summary>
internal sealed class DataApprovalScenarioService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IWorkflowPublishedDefinitionDirectory workflowDirectory)
{
    /// <summary>列出当前作用域内已登记场景及其绑定状态。</summary>
    public async Task<Result<IReadOnlyList<DataApprovalScenarioResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<DataApprovalScenarioRecord>(
                DataApprovalSql.ListScenarios,
                DataApprovalSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var bindingMap = rows.ToDictionary(row => row.ScenarioKey, StringComparer.Ordinal);
        var responses = DataApprovalScenarioCatalog.All
            .Where(entry => string.Equals(entry.ScopeKey, scope.ScopeKey, StringComparison.Ordinal))
            .Select(entry => Map(entry, bindingMap.GetValueOrDefault(entry.ScenarioKey)))
            .ToArray();
        return Result<IReadOnlyList<DataApprovalScenarioResponse>>.Success(responses);
    }

    /// <summary>读取单个场景的绑定状态。</summary>
    public async Task<Result<DataApprovalScenarioResponse>> GetAsync(
        string scenarioKey,
        CancellationToken cancellationToken = default)
    {
        var normalized = scenarioKey?.Trim() ?? string.Empty;
        var catalogEntry = DataApprovalScenarioCatalog.Find(normalized);
        if (catalogEntry is null)
        {
            return ScenarioNotFound();
        }

        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        if (!string.Equals(catalogEntry.ScopeKey, scope.ScopeKey, StringComparison.Ordinal))
        {
            return ScenarioNotFound();
        }

        var row = await FindBindingAsync(normalized, cancellationToken).ConfigureAwait(false);
        return Result<DataApprovalScenarioResponse>.Success(Map(catalogEntry, row));
    }

    /// <summary>更新场景启停状态与已发布工作流版本绑定。</summary>
    public async Task<Result<DataApprovalScenarioResponse>> UpdateBindingAsync(
        string scenarioKey,
        UpdateDataApprovalScenarioBindingBody request,
        CancellationToken cancellationToken = default)
    {
        var normalized = scenarioKey?.Trim() ?? string.Empty;
        var catalogEntry = DataApprovalScenarioCatalog.Find(normalized);
        if (catalogEntry is null)
        {
            return ScenarioNotFound();
        }

        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        if (!string.Equals(catalogEntry.ScopeKey, scope.ScopeKey, StringComparison.Ordinal))
        {
            return ScenarioNotFound();
        }

        string? workflowDefinitionKey = null;
        Guid? workflowDefinitionVersionId = null;
        if (request.IsEnabled)
        {
            if (request.WorkflowDefinitionVersionId is not { } versionId || versionId == Guid.Empty)
            {
                return Result<DataApprovalScenarioResponse>.Failure(new Error(
                    DataApprovalErrorCodes.RequestInvalid,
                    "A published workflow definition version is required when enabling a scenario.",
                    ErrorType.Validation));
            }

            var published = await workflowDirectory
                .FindPublishedByVersionIdAsync(versionId, cancellationToken)
                .ConfigureAwait(false);
            if (published is null)
            {
                return Result<DataApprovalScenarioResponse>.Failure(new Error(
                    DataApprovalErrorCodes.WorkflowDefinitionMissing,
                    "The workflow definition version is not published in the current scope.",
                    ErrorType.Validation));
            }

            workflowDefinitionKey = published.DefinitionKey;
            workflowDefinitionVersionId = published.DefinitionVersionId;
        }

        var existing = await FindBindingAsync(normalized, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        if (existing is null)
        {
            var id = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                    DataApprovalSql.InsertScenario,
                    DataApprovalSqlParameters.Create(
                        ("Id", id),
                        ("TenantId", scope.TenantId),
                        ("ScopeKey", scope.ScopeKey),
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("ScenarioKey", normalized),
                        ("IsEnabled", request.IsEnabled),
                        ("WorkflowDefinitionKey", workflowDefinitionKey),
                        ("WorkflowDefinitionVersionId", workflowDefinitionVersionId),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", now),
                        ("Version", 1L)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var expectedVersion = request.Version ?? existing.Version;
            var affected = await commandExecutor.ExecuteAsync(
                    DataApprovalSql.UpdateScenarioBinding,
                    DataApprovalSqlParameters.Create(
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("ScenarioKey", normalized),
                        ("IsEnabled", request.IsEnabled),
                        ("WorkflowDefinitionKey", workflowDefinitionKey),
                        ("WorkflowDefinitionVersionId", workflowDefinitionVersionId),
                        ("UpdatedAtUtc", now),
                        ("ExpectedVersion", expectedVersion)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return Result<DataApprovalScenarioResponse>.Failure(new Error(
                    DataApprovalErrorCodes.ScenarioConflict,
                    "The scenario binding could not be updated.",
                    ErrorType.Conflict));
            }
        }

        var updated = await FindBindingAsync(normalized, cancellationToken).ConfigureAwait(false);
        return Result<DataApprovalScenarioResponse>.Success(Map(catalogEntry, updated));
    }

    /// <summary>解析创建请求可用的已发布工作流版本；场景未启用或未配置时返回失败。</summary>
    internal async Task<Result<ResolvedScenarioBinding>> ResolveForCreateAsync(
        string scenarioKey,
        CancellationToken cancellationToken)
    {
        var catalogEntry = DataApprovalScenarioCatalog.Find(scenarioKey);
        if (catalogEntry is null)
        {
            return Result<ResolvedScenarioBinding>.Failure(new Error(
                DataApprovalErrorCodes.ScenarioUnsupported,
                "The approval scenario is not supported.",
                ErrorType.Validation));
        }

        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        if (!string.Equals(catalogEntry.ScopeKey, scope.ScopeKey, StringComparison.Ordinal))
        {
            return Result<ResolvedScenarioBinding>.Failure(new Error(
                DataApprovalErrorCodes.ScenarioUnsupported,
                "The approval scenario is not available in the current scope.",
                ErrorType.Validation));
        }

        var binding = await FindBindingAsync(scenarioKey, cancellationToken).ConfigureAwait(false);
        if (binding is null ||
            !binding.IsEnabled ||
            binding.WorkflowDefinitionVersionId is not { } versionId)
        {
            return Result<ResolvedScenarioBinding>.Failure(new Error(
                DataApprovalErrorCodes.ScenarioNotConfigured,
                "The approval scenario is disabled or not bound to a published workflow version.",
                ErrorType.Validation));
        }

        var published = await workflowDirectory
            .FindPublishedByVersionIdAsync(versionId, cancellationToken)
            .ConfigureAwait(false);
        if (published is null)
        {
            return Result<ResolvedScenarioBinding>.Failure(new Error(
                DataApprovalErrorCodes.WorkflowDefinitionMissing,
                "The bound workflow definition version is no longer published.",
                ErrorType.Validation));
        }

        return Result<ResolvedScenarioBinding>.Success(
            new ResolvedScenarioBinding(
                catalogEntry.WorkflowBusinessType,
                published.DefinitionVersionId));
    }

    private async Task<DataApprovalScenarioRecord?> FindBindingAsync(
        string scenarioKey,
        CancellationToken cancellationToken)
    {
        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        return await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalScenarioRecord>(
                DataApprovalSql.FindScenarioByKey,
                DataApprovalSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ScenarioKey", scenarioKey)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static DataApprovalScenarioResponse Map(
        DataApprovalScenarioCatalogEntry catalogEntry,
        DataApprovalScenarioRecord? row) =>
        new(
            catalogEntry.ScenarioKey,
            catalogEntry.ScopeKey,
            true,
            row?.IsEnabled ?? false,
            row?.WorkflowDefinitionKey,
            row?.WorkflowDefinitionVersionId,
            row?.Version);

    private static Result<DataApprovalScenarioResponse> ScenarioNotFound() =>
        Result<DataApprovalScenarioResponse>.Failure(new Error(
            DataApprovalErrorCodes.ScenarioNotFound,
            "The approval scenario was not found.",
            ErrorType.NotFound));
}

/// <summary>创建审批请求时解析得到的场景绑定摘要。</summary>
/// <param name="WorkflowBusinessType">启动工作流使用的业务类型机器码。</param>
/// <param name="WorkflowDefinitionVersionId">固定到请求上的已发布定义版本标识。</param>
internal sealed record ResolvedScenarioBinding(
    string WorkflowBusinessType,
    Guid WorkflowDefinitionVersionId);
