using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Mqtt.Contracts;
using Full.NET.Modules.Mqtt.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Mqtt.Features.ManageControlPlane;

/// <summary>MQTT 消息记录分页查询。</summary>
internal sealed class MqttMessageQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页查询 MQTT 消息记录。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="filter">过滤条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<MqttMessageResponse>>> ListAsync(
        int page,
        int pageSize,
        MqttMessageListFilter filter,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var topic = string.IsNullOrWhiteSpace(filter.Topic) ? null : filter.Topic.Trim();
        var parameters = MqttSqlParameters.Create(
            ("ClientId", filter.ClientId),
            ("Status", string.IsNullOrWhiteSpace(filter.Status) ? null : filter.Status.Trim()),
            ("Topic", topic),
            ("TopicPattern", topic is null ? null : $"%{topic}%"),
            ("FromUtc", filter.FromUtc),
            ("ToUtc", filter.ToUtc),
            ("Offset", offset),
            ("PageSize", pageSize));
        var countStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => MqttSql.CountMessagesSqlServer,
            DatabaseProvider.MySql => MqttSql.CountMessagesMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => MqttSql.ListMessagesSqlServer,
            DatabaseProvider.MySql => MqttSql.ListMessagesMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<MqttMessageRecord>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<MqttMessageResponse>>.Success(
            new PagedResult<MqttMessageResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>按标识查询单条 MQTT 消息记录。</summary>
    /// <param name="messageId">消息标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息详情或稳定未找到错误。</returns>
    public async Task<Result<MqttMessageResponse>> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<MqttMessageRecord>(
                MqttSql.FindMessageById,
                MqttSqlParameters.Create(("Id", messageId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<MqttMessageResponse>.Failure(new Error(
                MqttErrorCodes.MessageNotFound,
                "The MQTT message was not found.",
                ErrorType.NotFound))
            : Result<MqttMessageResponse>.Success(Map(record));
    }

    internal static MqttMessageResponse Map(MqttMessageRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.ClientId,
            record.ClientKey,
            record.Topic,
            record.PayloadSizeBytes,
            record.Qos,
            record.Status,
            record.IdempotencyKey,
            record.SummaryMessage,
            record.PublishedAtUtc,
            record.CreatedAtUtc,
            record.CreatedByUserId);
}
