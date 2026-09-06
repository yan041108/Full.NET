using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;
using Full.NET.Modules.K3Cloud.Persistence;

namespace Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;

/// <summary>K3Cloud 连接配置只读查询。</summary>
internal sealed class K3CloudConnectionQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<K3CloudConnectionConfigResponse>> GetByIdAsync(
        Guid connectionConfigId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<K3CloudConnectionConfigRecord>(
                K3CloudConnectionSql.FindById,
                K3CloudSqlParameters.Create([("ConnectionConfigId", connectionConfigId)]),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<K3CloudConnectionConfigResponse>.Success(K3CloudConnectionMapper.Map(row));
    }

    public async Task<Result<IReadOnlyList<K3CloudConnectionConfigResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<K3CloudConnectionConfigRecord>(
                K3CloudConnectionSql.List,
                K3CloudSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<K3CloudConnectionConfigResponse>>.Success(
            rows.Select(K3CloudConnectionMapper.Map).ToArray());
    }

    internal async Task<K3CloudConnectionConfigRecord?> FindRecordAsync(
        Guid connectionConfigId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<K3CloudConnectionConfigRecord>(
                K3CloudConnectionSql.FindById,
                K3CloudSqlParameters.Create([("ConnectionConfigId", connectionConfigId)]),
                cancellationToken)
            .ConfigureAwait(false);

    private static Result<K3CloudConnectionConfigResponse> NotFound() =>
        Result<K3CloudConnectionConfigResponse>.Failure(new Error(
            K3CloudErrorCodes.ConnectionNotFound,
            "The K3Cloud connection configuration was not found.",
            ErrorType.NotFound));
}
