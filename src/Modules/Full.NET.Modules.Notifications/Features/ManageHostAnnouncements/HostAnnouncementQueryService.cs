using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;

/// <summary>Host 公告分页查询。</summary>
internal sealed class HostAnnouncementQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 分页查询 Host 公告，按创建时间倒序排列。
    /// </summary>
    /// <remarks>
    /// 列表语句按当前数据库提供程序在 SQL Server 与 MySQL 实现间切换；
    /// 分页参数在服务端做上下界钳制，<c>OFFSET/FETCH</c> 与 <c>LIMIT/OFFSET</c> 由稳定排序保证稳定。
    /// </remarks>
    public async Task<Result<PagedResult<HostAnnouncementResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                AnnouncementSql.CountHost,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AnnouncementRecord>(
                ResolveListStatement(),
                new Dictionary<string, object?>
                {
                    ["Offset"] = offset,
                    ["PageSize"] = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostAnnouncementResponse>>.Success(
            new PagedResult<HostAnnouncementResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>
    /// 按标识查询单条 Host 公告；不存在时返回未找到错误。
    /// </summary>
    public async Task<Result<HostAnnouncementResponse>> GetByIdAsync(
        Guid announcementId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<AnnouncementRecord>(
                AnnouncementSql.FindHostById,
                new Dictionary<string, object?> { ["Id"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<HostAnnouncementResponse>.Success(Map(record));
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AnnouncementSql.ListHostSqlServer,
            DatabaseProvider.MySql => AnnouncementSql.ListHostMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };

    internal static HostAnnouncementResponse Map(AnnouncementRecord record) =>
        new(
            record.Id,
            record.Title,
            record.Content,
            record.Status,
            record.PublishedAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<HostAnnouncementResponse> NotFound() =>
        Result<HostAnnouncementResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementNotFound,
            "The host announcement was not found.",
            ErrorType.NotFound));
}
