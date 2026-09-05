using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>在可信作用域内分页查询当前用户的待办与已办历史。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="databaseOptions">数据库提供程序配置。</param>
internal sealed class WorkflowTodoQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页列出当前用户的活动待办。</summary>
    /// <param name="actorUserId">可信当前用户标识。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数，限制在 1～100。</param>
    /// <param name="definitionKey">可选定义键筛选。</param>
    /// <param name="businessType">可选业务类型筛选。</param>
    /// <param name="arrivedFromUtc">可选到达起始时间（UTC）。</param>
    /// <param name="arrivedToUtc">可选到达结束时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>当前页待办或稳定业务错误。</returns>
    public Task<Result<PagedResult<WorkflowTodoListItemResponse>>> ListPendingAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        string? definitionKey,
        string? businessType,
        DateTimeOffset? arrivedFromUtc,
        DateTimeOffset? arrivedToUtc,
        CancellationToken cancellationToken) =>
        ListCoreAsync(
            actorUserId,
            page,
            pageSize,
            definitionKey,
            businessType,
            resultActionKey: null,
            arrivedFromUtc,
            arrivedToUtc,
            completedFromUtc: null,
            completedToUtc: null,
            isHistory: false,
            cancellationToken);

    /// <summary>分页列出当前用户已办历史；结果基于完成时写入的快照字段，不依赖当前活动待办状态。</summary>
    /// <param name="actorUserId">可信当前用户标识。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数，限制在 1～100。</param>
    /// <param name="definitionKey">可选定义键筛选。</param>
    /// <param name="businessType">可选业务类型筛选。</param>
    /// <param name="resultActionKey">可选处理结果动作筛选。</param>
    /// <param name="completedFromUtc">可选完成起始时间（UTC）。</param>
    /// <param name="completedToUtc">可选完成结束时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>当前页已办或稳定业务错误。</returns>
    public Task<Result<PagedResult<WorkflowTodoListItemResponse>>> ListHistoryAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        string? definitionKey,
        string? businessType,
        string? resultActionKey,
        DateTimeOffset? completedFromUtc,
        DateTimeOffset? completedToUtc,
        CancellationToken cancellationToken) =>
        ListCoreAsync(
            actorUserId,
            page,
            pageSize,
            definitionKey,
            businessType,
            resultActionKey,
            arrivedFromUtc: null,
            arrivedToUtc: null,
            completedFromUtc,
            completedToUtc,
            isHistory: true,
            cancellationToken);

    private async Task<Result<PagedResult<WorkflowTodoListItemResponse>>> ListCoreAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        string? definitionKey,
        string? businessType,
        string? resultActionKey,
        DateTimeOffset? arrivedFromUtc,
        DateTimeOffset? arrivedToUtc,
        DateTimeOffset? completedFromUtc,
        DateTimeOffset? completedToUtc,
        bool isHistory,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var normalizedDefinitionKey = WorkflowTodoListRules.NormalizeDefinitionKey(definitionKey);
        var normalizedBusinessType = WorkflowTodoListRules.NormalizeBusinessType(businessType);
        var normalizedResultActionKey = string.IsNullOrWhiteSpace(resultActionKey)
            ? null
            : resultActionKey.Trim();
        if (!WorkflowTodoListRules.IsValidResultActionKey(normalizedResultActionKey)
            || !WorkflowTodoListRules.IsValidTimeRange(arrivedFromUtc, arrivedToUtc)
            || !WorkflowTodoListRules.IsValidTimeRange(completedFromUtc, completedToUtc))
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var parameters = WorkflowSqlParameters.Create(
            ("TenantScopeKey", scope.TenantScopeKey),
            ("AssigneeUserId", actorUserId),
            ("DefinitionKey", normalizedDefinitionKey),
            ("BusinessType", normalizedBusinessType),
            ("ResultActionKey", normalizedResultActionKey),
            ("ArrivedFromUtc", arrivedFromUtc),
            ("ArrivedToUtc", arrivedToUtc),
            ("CompletedFromUtc", completedFromUtc),
            ("CompletedToUtc", completedToUtc),
            ("Offset", offset),
            ("PageSize", pageSize));

        var countStatement = isHistory
            ? WorkflowSql.CountHistoryTodosFiltered
            : WorkflowSql.CountPendingTodosFiltered;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var pageStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => isHistory
                ? WorkflowSql.PageHistoryTodosFilteredSqlServer
                : WorkflowSql.PagePendingTodosFilteredSqlServer,
            DatabaseProvider.MySql => isHistory
                ? WorkflowSql.PageHistoryTodosFilteredMySql
                : WorkflowSql.PagePendingTodosFilteredMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        var rows = await queryExecutor.QueryAsync<WorkflowTodoListRecord>(
                pageStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<WorkflowTodoListItemResponse>>.Success(
            new PagedResult<WorkflowTodoListItemResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    private static WorkflowTodoListItemResponse Map(WorkflowTodoListRecord record) =>
        new(
            record.Id,
            record.InstanceId,
            record.StepId,
            record.StatusKey,
            record.ArrivedAtUtc,
            record.CompletedAtUtc,
            record.ResultActionKey,
            record.Revision,
            record.BusinessType,
            record.BusinessId,
            record.BusinessTitle,
            record.InstanceStatusKey,
            record.DefinitionKey,
            record.NodeKey);

    private static Result<PagedResult<WorkflowTodoListItemResponse>> Failure(string code, ErrorType type) =>
        Result<PagedResult<WorkflowTodoListItemResponse>>.Failure(
            new Error(code, "The workflow todo list query failed.", type));
}
