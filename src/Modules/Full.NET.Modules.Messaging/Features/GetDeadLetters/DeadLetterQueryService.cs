using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Features.GetDeadLetters;

/// <summary>
/// 分页查询消费死信，可按消费者名过滤，用于运维排查消费失败与重放决策。
/// </summary>
internal sealed class DeadLetterQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 分页查询死信，按接收时间倒序排列，可按消费者名过滤。
    /// </summary>
    /// <remarks>
    /// 列表语句按当前数据库提供程序在 SQL Server 与 MySQL 实现间切换；
    /// 分页参数在服务端做上下界钳制，<c>consumerName</c> 为空时返回全部消费者死信。
    /// </remarks>
    public async Task<Result<PagedResult<DeadLetterResponse>>> ListAsync(
        int page,
        int pageSize,
        string? consumerName,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                MessagingOperationsSql.CountDeadLetters,
                new { ConsumerName = consumerName },
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<DeadLetterRecord>(
                ResolveListStatement(),
                new
                {
                    Offset = offset,
                    PageSize = pageSize,
                    ConsumerName = consumerName,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<DeadLetterResponse>>.Success(
            new PagedResult<DeadLetterResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => MessagingOperationsSql.ListDeadLettersSqlServer,
            DatabaseProvider.MySql => MessagingOperationsSql.ListDeadLettersMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };

    internal static DeadLetterResponse Map(DeadLetterRecord record) =>
        new(
            record.ConsumerName,
            record.MessageId,
            record.MessageType,
            record.SchemaVersion,
            record.TenantId,
            record.Attempts,
            record.ReceivedAtUtc,
            record.LastErrorCode,
            record.LastError);
}
