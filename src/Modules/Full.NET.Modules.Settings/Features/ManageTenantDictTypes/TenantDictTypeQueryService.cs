using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageTenantDictTypes;

/// <summary>租户数据字典类型分页列表与详情只读查询。</summary>
internal sealed class TenantDictTypeQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<DictTypeResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantDictTypeSql.CountTenantDictTypes,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantDictTypeSql.ListTenantDictTypesSqlServer,
            DatabaseProvider.MySql => TenantDictTypeSql.ListTenantDictTypesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<DictTypeRecord>(
                statement,
                new { Offset = offset, PageSize = pageSize },
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
                TenantDictTypeSql.FindById,
                new { DictTypeId = dictTypeId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<DictTypeResponse>.Success(Map(record));
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
