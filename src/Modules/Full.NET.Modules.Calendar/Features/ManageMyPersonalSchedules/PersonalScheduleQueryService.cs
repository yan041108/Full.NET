using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Calendar.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Calendar.Features.ManageMyPersonalSchedules;

/// <summary>当前用户个人日程分页查询；作用域只来自受信会话。</summary>
internal sealed class PersonalScheduleQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 按所属用户与当前作用域分页查询个人日程，默认按开始时间升序排列。
    /// </summary>
    /// <param name="ownerUserId">所属用户标识，必须来自受信 sub 声明。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">单页条数，服务端钳制为 1～100。</param>
    /// <param name="status">可选状态过滤，非法值会被忽略。</param>
    /// <param name="fromUtc">可选区间起点；与 toUtc 同时提供时启用重叠过滤。</param>
    /// <param name="toUtc">可选区间终点；与 fromUtc 同时提供时启用重叠过滤。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>分页结果或稳定业务错误。</returns>
    public async Task<Result<PagedResult<PersonalScheduleResponse>>> ListAsync(
        Guid ownerUserId,
        int page,
        int pageSize,
        string? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var scope = CalendarScope.Resolve(currentTenant);
        var normalizedStatus = PersonalScheduleManagementService.NormalizeStatusFilter(status);
        DateTimeOffset? rangeFrom = null;
        DateTimeOffset? rangeTo = null;
        if (fromUtc is not null && toUtc is not null)
        {
            rangeFrom = fromUtc.Value.ToUniversalTime();
            rangeTo = toUtc.Value.ToUniversalTime();
        }

        var parameters = CalendarSqlParameters.Create(
            ("OwnerUserId", ownerUserId),
            ("ScopeTenantId", scope.TenantId),
            ("Offset", offset),
            ("PageSize", pageSize),
            ("Status", normalizedStatus),
            ("FromUtc", rangeFrom),
            ("ToUtc", rangeTo));

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                PersonalScheduleSql.CountForOwner,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<PersonalScheduleRecord>(
                ResolveListStatement(),
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<PersonalScheduleResponse>>.Success(
            new PagedResult<PersonalScheduleResponse>(
                rows.Select(PersonalScheduleManagementService.Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>
    /// 读取当前用户作用域内的单条个人日程详情。
    /// </summary>
    /// <param name="ownerUserId">所属用户标识，必须来自受信 sub 声明。</param>
    /// <param name="scheduleId">个人日程标识。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>详情响应或 not found 错误。</returns>
    public async Task<Result<PersonalScheduleResponse>> GetAsync(
        Guid ownerUserId,
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        var scope = CalendarScope.Resolve(currentTenant);
        var record = await queryExecutor.QuerySingleOrDefaultAsync<PersonalScheduleRecord>(
                PersonalScheduleSql.FindForOwnerById,
                CalendarSqlParameters.Create(
                    ("Id", scheduleId),
                    ("OwnerUserId", ownerUserId),
                    ("ScopeTenantId", scope.TenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? PersonalScheduleManagementService.NotFound<PersonalScheduleResponse>()
            : Result<PersonalScheduleResponse>.Success(PersonalScheduleManagementService.Map(record));
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => PersonalScheduleSql.ListSqlServer,
            DatabaseProvider.MySql => PersonalScheduleSql.ListMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
}
