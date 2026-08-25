using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostDictTypes;
using Full.NET.Modules.Settings.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageHostDictItems;

/// <summary>Host 数据字典项分页列表与详情只读查询。</summary>
internal sealed class HostDictItemQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<DictItemResponse>>> ListByTypeIdAsync(
        Guid dictTypeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var typeExists = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                DictTypeSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (typeExists is null)
        {
            return TypeNotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DictItemSql.CountByTypeId,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DictItemSql.ListByTypeIdSqlServer,
            DatabaseProvider.MySql => DictItemSql.ListByTypeIdMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<DictItemRecord>(
                statement,
                SettingsSqlParameters.Create(
                    ("DictTypeId", dictTypeId),
                    ("Offset", offset),
                    ("PageSize", pageSize)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<DictItemResponse>>.Success(
            new PagedResult<DictItemResponse>(items, page, pageSize, total));
    }

    public async Task<Result<DictItemResponse>> GetByIdAsync(
        Guid dictItemId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<DictItemRecord>(
                DictItemSql.FindById,
                SettingsSqlParameters.Create(("DictItemId", dictItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<DictItemResponse>.Success(Map(record));
    }

    /// <summary>
    /// 按字典类型编码查询启用字典项，对应 Admin.NET dataList by code。
    /// 仅返回 IsActive=1 的项，按 DisplayOrder/Label 排序，供业务模块高频消费。
    /// </summary>
    public async Task<Result<IReadOnlyList<DictItemResponse>>> ListByTypeCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedCode.Length == 0)
        {
            return Result<IReadOnlyList<DictItemResponse>>.Success(
                Array.Empty<DictItemResponse>());
        }

        var rows = await queryExecutor.QueryAsync<DictItemRecord>(
                DictItemSql.ListByTypeCode,
                SettingsSqlParameters.Create(("Code", normalizedCode)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<IReadOnlyList<DictItemResponse>>.Success(items);
    }

    internal static DictItemResponse Map(DictItemRecord record) =>
        new(
            record.Id,
            record.DictTypeId,
            record.Label,
            record.Value,
            record.Color,
            record.DisplayOrder,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<PagedResult<DictItemResponse>> TypeNotFound() =>
        Result<PagedResult<DictItemResponse>>.Failure(new Error(
            SettingsErrorCodes.DictTypeNotFound,
            "The dictionary type was not found.",
            ErrorType.NotFound));

    private static Result<DictItemResponse> NotFound() =>
        Result<DictItemResponse>.Failure(new Error(
            SettingsErrorCodes.DictItemNotFound,
            "The dictionary item was not found.",
            ErrorType.NotFound));
}

internal sealed record DictItemRecord(
    Guid Id,
    Guid DictTypeId,
    string Label,
    string Value,
    string? Color,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record DictItemIdentityRecord(
    Guid Id,
    Guid DictTypeId,
    string Label,
    string Value,
    string? Color,
    int DisplayOrder,
    bool IsActive,
    int Version);
