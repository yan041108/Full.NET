using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Mqtt.Contracts;
using Full.NET.Modules.Mqtt.Persistence;

namespace Full.NET.Modules.Mqtt.Features.ManageControlPlane;

/// <summary>MQTT 客户端目录查询。</summary>
internal sealed class MqttClientQueryService(IQueryExecutor queryExecutor)
{
    /// <summary>列出全部 MQTT 客户端目录项。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>客户端目录列表。</returns>
    public async Task<Result<IReadOnlyList<MqttClientResponse>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<MqttClientRecord>(
                MqttSql.ListClients,
                MqttSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<MqttClientResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    /// <summary>按标识查询单条 MQTT 客户端。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>客户端详情或稳定未找到错误。</returns>
    public async Task<Result<MqttClientResponse>> GetByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<MqttClientRecord>(
                MqttSql.FindClientById,
                MqttSqlParameters.Create(("Id", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<MqttClientResponse>.Failure(new Error(
                MqttErrorCodes.ClientNotFound,
                "The MQTT client was not found.",
                ErrorType.NotFound))
            : Result<MqttClientResponse>.Success(Map(record));
    }

    internal static MqttClientResponse Map(MqttClientRecord record) =>
        new(
            record.Id,
            record.ClientKey,
            record.DisplayName,
            record.Description,
            record.TenantId,
            record.IsEnabled,
            record.SortOrder,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);
}
