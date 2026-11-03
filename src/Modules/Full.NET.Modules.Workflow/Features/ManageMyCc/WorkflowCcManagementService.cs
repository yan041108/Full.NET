using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageMyCc;

/// <summary>在可信租户和当前用户双边界内查询并标记工作流抄送记录。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="databaseOptions">数据库提供程序配置。</param>
internal sealed class WorkflowCcManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICurrentTenant currentTenant,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions, ICommandTransaction transaction, IActiveTenantContextResolver tenants)
{
    /// <summary>读取当前用户最近 100 条抄送记录。</summary>
    /// <param name="actorUserId">可信当前用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>按到达时间倒序排列的抄送记录。</returns>
    public async Task<Result<IReadOnlyList<WorkflowCcResponse>>> ListMineAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowSql.ListMyCcSqlServer,
            DatabaseProvider.MySql => WorkflowSql.ListMyCcMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        var rows = await queryExecutor.QueryAsync<WorkflowCcRecord>(
            statement,
            WorkflowSqlParameters.Create(
                ("TenantScopeKey", scope.TenantScopeKey),
                ("RecipientUserId", actorUserId),
                ("Take", 100)),
            cancellationToken).ConfigureAwait(false);
        var visible = new List<WorkflowCcResponse>(rows.Count);
        foreach (var row in rows)
            if (await WorkflowCollaborationRuntimeGate.AllowsAsync(row.InstanceId, scope, actorUserId, clock.UtcNow, queryExecutor,
                    cancellationToken, tenants: tenants).ConfigureAwait(false)) visible.Add(Map(row));
        return Result<IReadOnlyList<WorkflowCcResponse>>.Success(visible);
    }

    /// <summary>幂等标记当前用户自己的抄送记录为已读。</summary>
    /// <param name="ccId">抄送记录标识。</param>
    /// <param name="actorUserId">可信当前用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>首次已读时间；记录越界时返回不泄露存在性的 NotFound。</returns>
    public async Task<Result<WorkflowCcReadResponse>> MarkReadAsync(
        Guid ccId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var owned = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCcRecord>(WorkflowSql.FindOwnCcById,
            WorkflowSqlParameters.Create(("Id", ccId), ("RecipientUserId", actorUserId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        if (owned is null || !await WorkflowCollaborationRuntimeGate.AllowsAsync(owned.InstanceId, scope, actorUserId, clock.UtcNow,
                queryExecutor, cancellationToken, tenants: tenants).ConfigureAwait(false)) return NotFound();
        if (owned.ReadAtUtc is { } readAt) return Result<WorkflowCcReadResponse>.Success(new(owned.Id, readAt));
        return await transaction.ExecuteResultAsync(async inner =>
        {
            if (await commandExecutor.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    WorkflowSqlParameters.Create(("Id", owned.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1 ||
                !await WorkflowCollaborationRuntimeGate.AllowsAsync(owned.InstanceId, scope, actorUserId, clock.UtcNow, queryExecutor, inner, commandExecutor).ConfigureAwait(false)) return NotFound();
            return await MarkReadCoreAsync(ccId, actorUserId, inner).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowCcReadResponse>> MarkReadCoreAsync(Guid ccId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var parameters = WorkflowSqlParameters.Create(
            ("Id", ccId),
            ("RecipientUserId", actorUserId),
            ("TenantScopeKey", scope.TenantScopeKey));
        var record = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCcRecord>(
            WorkflowSql.FindOwnCcById,
            parameters,
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.ReadAtUtc is { } existingReadAtUtc)
        {
            return Result<WorkflowCcReadResponse>.Success(new(record.Id, existingReadAtUtc));
        }

        var readAtUtc = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
            WorkflowSql.MarkOwnCcRead,
            WorkflowSqlParameters.Create(
                ("Id", ccId),
                ("RecipientUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey),
                ("ReadAtUtc", readAtUtc)),
            cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var replay = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCcRecord>(
                WorkflowSql.FindOwnCcById,
                parameters,
                cancellationToken).ConfigureAwait(false);
            return replay?.ReadAtUtc is { } replayReadAtUtc
                ? Result<WorkflowCcReadResponse>.Success(new(replay.Id, replayReadAtUtc))
                : NotFound();
        }

        return Result<WorkflowCcReadResponse>.Success(new(ccId, readAtUtc));
    }

    /// <summary>把持久化投影转换为不暴露接收人权威字段的 API 响应。</summary>
    /// <param name="record">当前用户已授权的持久化投影。</param>
    /// <returns>抄送 API 响应。</returns>
    private static WorkflowCcResponse Map(WorkflowCcRecord record) =>
        new(
            record.Id,
            record.InstanceId,
            record.StepId,
            record.NodeKey,
            record.BusinessType,
            record.BusinessId,
            record.BusinessTitle,
            record.CreatedAtUtc,
            record.ReadAtUtc);

    /// <summary>构造不泄露跨用户记录存在性的稳定 NotFound 结果。</summary>
    /// <returns>抄送记录不可见错误。</returns>
    private static Result<WorkflowCcReadResponse> NotFound() =>
        Result<WorkflowCcReadResponse>.Failure(new Error(
            WorkflowErrorCodes.CcNotFound,
            "The workflow CC record was not found.",
            ErrorType.NotFound));
}
