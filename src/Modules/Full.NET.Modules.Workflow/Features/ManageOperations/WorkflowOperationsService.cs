using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageOperations;

/// <summary>单次请求固定统计时钟，服务端有界分页并复用当前协作授权；无跨模块表访问或恢复副作用。</summary>
internal sealed class WorkflowOperationsService(IQueryExecutor query, ICurrentTenant tenant, IClock clock,
    IOptions<DatabaseOptions> database, WorkflowCollaborationListAuthorization authorization)
{
    public async Task<Result<WorkflowOperationsStepsResponse>> StepsAsync(Guid actor, int page, int pageSize,
        Guid? definitionVersionId, long minimumDwellSeconds, bool onlyOverdue, CancellationToken token)
    {
        if (!Valid(actor, page, pageSize, definitionVersionId) || minimumDwellSeconds is < 0 or > 31_536_000)
            return Failure<WorkflowOperationsStepsResponse>();
        var now = clock.UtcNow;
        var parameters = Parameters(page, pageSize, definitionVersionId);
        parameters["StartedBeforeUtc"] = now.AddSeconds(-minimumDwellSeconds); parameters["OnlyOverdue"] = onlyOverdue ? 1 : 0;
        var proof = await authorization.PrepareAsync("instance", actor, parameters, token).ConfigureAwait(false);
        if (!proof.IsSuccess) return Result<WorkflowOperationsStepsResponse>.Failure(proof.Error!);
        parameters["CollaborationProofs"] = proof.Value; parameters["Now"] = now;
        var mysql = database.Value.Provider == DatabaseProvider.MySql;
        var total = await query.QuerySingleOrDefaultAsync<long>(mysql ? WorkflowOperationsSql.CountStepsMySql : WorkflowOperationsSql.CountStepsSqlServer, parameters, token).ConfigureAwait(false);
        var rows = await query.QueryAsync<WorkflowOperationsStepRecord>(mysql ? WorkflowOperationsSql.PageStepsMySql : WorkflowOperationsSql.PageStepsSqlServer, parameters, token).ConfigureAwait(false);
        return Result<WorkflowOperationsStepsResponse>.Success(new(now, rows.Select(row => new WorkflowOperationsStepResponse(
            row.Id, row.InstanceId, row.DefinitionVersionId, row.DefinitionKey, row.NodeKey, row.NodeTypeKey, row.InstanceStatusKey,
            row.StatusKey, row.StartedAtUtc, Math.Max(0, (long)(now - row.StartedAtUtc).TotalSeconds), row.DueAtUtc,
            row.PausedAtUtc, row.SlaStatusKey, row.InstanceStatusKey == "active" && row.StatusKey == "active" &&
                row.SlaStatusKey == "waiting" && row.PausedAtUtc is null && row.DueAtUtc <= now)).ToArray(), page, pageSize, total));
    }

    public async Task<Result<WorkflowOperationsBacklogResponse>> BacklogAsync(Guid actor, int page, int pageSize,
        Guid? definitionVersionId, CancellationToken token)
    {
        if (!Valid(actor, page, pageSize, definitionVersionId)) return Failure<WorkflowOperationsBacklogResponse>();
        var now = clock.UtcNow; var parameters = Parameters(page, pageSize, definitionVersionId);
        var proof = await authorization.PrepareAsync("instance", actor, parameters, token).ConfigureAwait(false);
        if (!proof.IsSuccess) return Result<WorkflowOperationsBacklogResponse>.Failure(proof.Error!);
        parameters["CollaborationProofs"] = proof.Value; parameters["Now"] = now;
        var mysql = database.Value.Provider == DatabaseProvider.MySql;
        var total = await query.QuerySingleOrDefaultAsync<long>(mysql ? WorkflowOperationsSql.CountBacklogMySql : WorkflowOperationsSql.CountBacklogSqlServer, parameters, token).ConfigureAwait(false);
        var rows = await query.QueryAsync<WorkflowOperationsWorkRecord>(mysql ? WorkflowOperationsSql.PageBacklogMySql : WorkflowOperationsSql.PageBacklogSqlServer, parameters, token).ConfigureAwait(false);
        return Result<WorkflowOperationsBacklogResponse>.Success(new(now, rows.Select(row => new WorkflowOperationsWorkResponse(
            row.Id, row.InstanceId, row.DefinitionVersionId, row.DefinitionKey, row.NodeKey, row.StatusKey, row.InstanceStatusKey,
            row.DueAtUtc, row.LeaseExpiresAtUtc, row.AttemptCount, IsReady(row, now))).ToArray(), page, pageSize, total));
    }

    internal static bool IsReady(WorkflowOperationsWorkRecord row, DateTimeOffset now) => row.InstanceStatusKey == "active" && row.DueAtUtc <= now &&
        (row.StatusKey == "pending" || row.StatusKey == "leased" && row.LeaseExpiresAtUtc <= now);
    private static bool Valid(Guid actor, int page, int size, Guid? version) => actor != Guid.Empty && page is >= 1 and <= 100_000 && size is >= 1 and <= 100 && version != Guid.Empty;
    private Dictionary<string, object?> Parameters(int page, int size, Guid? version) => WorkflowSqlParameters.Create(
        ("TenantScopeKey", WorkflowManagementScope.Resolve(tenant).TenantScopeKey), ("Offset", (page - 1) * size), ("PageSize", size), ("DefinitionVersionId", version));
    private static Result<T> Failure<T>() => Result<T>.Failure(new(WorkflowErrorCodes.SchemaInvalid, "The workflow operations query is invalid.", ErrorType.Validation));
}

/// <summary>停留为当前激活的墙钟秒数，包含暂停时间；暂停SLA不计逾期。</summary>
internal sealed record WorkflowOperationsStepResponse(Guid Id, Guid InstanceId, Guid DefinitionVersionId, string DefinitionKey,
    string NodeKey, string NodeTypeKey, string InstanceStatusKey, string StatusKey, DateTimeOffset StartedAtUtc, long DwellSeconds,
    DateTimeOffset? DueAtUtc, DateTimeOffset? PausedAtUtc, string? SlaStatusKey, bool IsOverdue);
/// <summary>固定观察时刻的步骤分页，不将COUNT和分页声称为事务快照。</summary>
internal sealed record WorkflowOperationsStepsResponse(DateTimeOffset ObservedAtUtc, IReadOnlyList<WorkflowOperationsStepResponse> Items, int Page, int PageSize, long Total);
/// <summary>不暴露输入、租约持有人或业务错误详情；人工恢复必须另行重读精确资源。</summary>
internal sealed record WorkflowOperationsWorkResponse(Guid Id, Guid InstanceId, Guid DefinitionVersionId, string DefinitionKey,
    string NodeKey, string StatusKey, string InstanceStatusKey, DateTimeOffset DueAtUtc, DateTimeOffset? LeaseExpiresAtUtc, int AttemptCount, bool IsReady);
/// <summary>包括未来、已租用和失败工作；IsReady区分观察时刻可由Worker领取的项。</summary>
internal sealed record WorkflowOperationsBacklogResponse(DateTimeOffset ObservedAtUtc, IReadOnlyList<WorkflowOperationsWorkResponse> Items, int Page, int PageSize, long Total);
