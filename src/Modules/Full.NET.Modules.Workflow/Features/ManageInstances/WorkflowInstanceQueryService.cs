using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

/// <summary>在可信作用域内分页查询工作流实例列表。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="databaseOptions">数据库提供程序配置。</param>
internal sealed class WorkflowInstanceQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页列出当前作用域内全部实例，供全局管理使用。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数，限制在 1～100。</param>
    /// <param name="statusKey">可选状态筛选。</param>
    /// <param name="definitionKey">可选定义键筛选。</param>
    /// <param name="definitionVersionId">可选定义版本筛选。</param>
    /// <param name="startedFromUtc">可选发起起始时间（UTC）。</param>
    /// <param name="startedToUtc">可选发起结束时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>当前页实例或稳定业务错误。</returns>
    public Task<Result<PagedResult<WorkflowInstanceListItemResponse>>> ListAsync(
        int page,
        int pageSize,
        string? statusKey,
        string? definitionKey,
        Guid? definitionVersionId,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToUtc,
        CancellationToken cancellationToken) =>
        ListCoreAsync(
            page,
            pageSize,
            startedById: null,
            statusKey,
            definitionKey,
            definitionVersionId,
            startedFromUtc,
            startedToUtc,
            cancellationToken);

    /// <summary>分页列出当前用户发起的实例，服务端固定发起人筛选。</summary>
    /// <param name="actorUserId">可信当前用户标识。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数，限制在 1～100。</param>
    /// <param name="statusKey">可选状态筛选。</param>
    /// <param name="definitionKey">可选定义键筛选。</param>
    /// <param name="definitionVersionId">可选定义版本筛选。</param>
    /// <param name="startedFromUtc">可选发起起始时间（UTC）。</param>
    /// <param name="startedToUtc">可选发起结束时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>当前页实例或稳定业务错误。</returns>
    public Task<Result<PagedResult<WorkflowInstanceListItemResponse>>> ListMineAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        string? statusKey,
        string? definitionKey,
        Guid? definitionVersionId,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToUtc,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Task.FromResult(Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation));
        }

        return ListCoreAsync(
            page,
            pageSize,
            actorUserId,
            statusKey,
            definitionKey,
            definitionVersionId,
            startedFromUtc,
            startedToUtc,
            cancellationToken);
    }

    private async Task<Result<PagedResult<WorkflowInstanceListItemResponse>>> ListCoreAsync(
        int page,
        int pageSize,
        Guid? startedById,
        string? statusKey,
        string? definitionKey,
        Guid? definitionVersionId,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToUtc,
        CancellationToken cancellationToken)
    {
        var normalizedStatusKey = string.IsNullOrWhiteSpace(statusKey) ? null : statusKey.Trim();
        var normalizedDefinitionKey = WorkflowInstanceListRules.NormalizeDefinitionKey(definitionKey);
        if (!WorkflowInstanceListRules.IsValidStatusKey(normalizedStatusKey)
            || !WorkflowInstanceListRules.IsValidTimeRange(startedFromUtc, startedToUtc)
            || definitionVersionId == Guid.Empty)
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var parameters = WorkflowSqlParameters.Create(
            ("TenantScopeKey", scope.TenantScopeKey),
            ("StartedById", startedById),
            ("StatusKey", normalizedStatusKey),
            ("DefinitionKey", normalizedDefinitionKey),
            ("DefinitionVersionId", definitionVersionId),
            ("StartedFromUtc", startedFromUtc),
            ("StartedToUtc", startedToUtc),
            ("Offset", offset),
            ("PageSize", pageSize));

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowSql.CountInstancesFiltered,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? WorkflowSql.PageInstancesFilteredMySql
            : WorkflowSql.PageInstancesFilteredSqlServer;
        var rows = await queryExecutor.QueryAsync<WorkflowInstanceListRecord>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<WorkflowInstanceListItemResponse>>.Success(
            new PagedResult<WorkflowInstanceListItemResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    private static WorkflowInstanceListItemResponse Map(WorkflowInstanceListRecord record) =>
        new(
            record.Id,
            record.DefinitionVersionId,
            record.DefinitionKey,
            record.BusinessType,
            record.BusinessId,
            record.BusinessTitle,
            record.StatusKey,
            record.StartedById,
            record.StartedAtUtc,
            record.CompletedAtUtc);

    private static Result<PagedResult<WorkflowInstanceListItemResponse>> Failure(string code, ErrorType type) =>
        Result<PagedResult<WorkflowInstanceListItemResponse>>.Failure(
            new Error(code, "The workflow instance list query failed.", type));
}
