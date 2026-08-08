using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Features.GetDeadLetters;

internal sealed class DeadLetterQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
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
