using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageHostDictTypes;

/// <summary>Host 数据字典类型分页列表与详情只读查询。</summary>
internal sealed class HostDictTypeQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<DictTypeResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default, HostDictTypeListFilter? filter = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["Offset"] = ((long)Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 100),
            ["PageSize"] = Math.Clamp(pageSize, 1, 100),
            ["Code"] = SqlSearchPattern.ContainsLiteral(filter?.Code),
            ["Name"] = SqlSearchPattern.ContainsLiteral(filter?.Name),
            ["Status"] = string.IsNullOrWhiteSpace(filter?.Status) ? null : filter.Status.Trim(),
        };
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DictTypeSql.CountHostDictTypesFiltered,
                parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DictTypeSql.ListHostDictTypesSqlServerFiltered,
            DatabaseProvider.MySql => DictTypeSql.ListHostDictTypesMySqlFiltered,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<DictTypeRecord>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<DictTypeResponse>>.Success(
            new PagedResult<DictTypeResponse>(items, page, pageSize, total));
    }

    public async Task<Result<DictTypeResponse>> GetByIdAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeRecord>(
                DictTypeSql.FindById,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<DictTypeResponse>.Success(Map(record));
    }

    /// <summary>
    /// 全量 Host 字典类型列表（不分页），对应 Admin.NET queryDictTypeList 全量场景，
    /// 供下拉选择与全量消费使用。
    /// </summary>
    public async Task<Result<IReadOnlyList<DictTypeResponse>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<DictTypeRecord>(
                DictTypeSql.ListAllHostDictTypes,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<IReadOnlyList<DictTypeResponse>>.Success(items);
    }

    internal static DictTypeResponse Map(DictTypeRecord record) =>
        new(
            record.Id,
            record.Code,
            record.Name,
            record.Description,
            record.DisplayOrder,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<DictTypeResponse> NotFound() =>
        Result<DictTypeResponse>.Failure(new Error(
            SettingsErrorCodes.DictTypeNotFound,
            "The dictionary type was not found.",
            ErrorType.NotFound));
}

internal sealed record DictTypeRecord(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record DictTypeIdentityRecord(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int DisplayOrder,
    bool IsActive,
    int Version);

/// <summary>字典目录分页筛选。</summary>
internal sealed record HostDictTypeListFilter(string? Code = null, string? Name = null, string? Status = null);
