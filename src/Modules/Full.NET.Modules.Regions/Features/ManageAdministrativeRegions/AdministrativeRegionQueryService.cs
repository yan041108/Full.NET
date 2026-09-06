using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Regions.Contracts;
using Full.NET.Modules.Regions.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Regions.Features.ManageAdministrativeRegions;

/// <summary>行政区域查询服务：分页列表、详情、级联子节点与树查询。</summary>
internal sealed class AdministrativeRegionQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页查询行政区域扁平列表。</summary>
    public async Task<Result<PagedResult<AdministrativeRegionResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? parentId,
        string? name,
        string? code,
        int? level,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = RegionsSqlParameters.Create(
            ("ParentId", parentId),
            ("Name", string.IsNullOrWhiteSpace(name) ? null : name.Trim()),
            ("NamePattern", string.IsNullOrWhiteSpace(name) ? null : $"%{name.Trim()}%"),
            ("Code", string.IsNullOrWhiteSpace(code) ? null : code.Trim()),
            ("CodePattern", string.IsNullOrWhiteSpace(code) ? null : $"%{code.Trim()}%"),
            ("Level", level),
            ("Offset", offset),
            ("PageSize", pageSize));

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                AdministrativeRegionSql.Count,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AdministrativeRegionRecord>(
                ResolveListStatement(),
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<AdministrativeRegionResponse>>.Success(
            new PagedResult<AdministrativeRegionResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>按标识查询行政区域详情。</summary>
    public async Task<Result<AdministrativeRegionResponse>> GetByIdAsync(
        Guid regionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<AdministrativeRegionRecord>(
                AdministrativeRegionSql.FindById,
                RegionsSqlParameters.Create(("Id", regionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<AdministrativeRegionResponse>.Success(Map(record));
    }

    /// <summary>查询直接子节点，供级联选择器使用。</summary>
    public async Task<Result<IReadOnlyList<AdministrativeRegionChildResponse>>> ListChildrenAsync(
        Guid? parentId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<AdministrativeRegionChildQueryRecord>(
                AdministrativeRegionSql.ListChildren,
                RegionsSqlParameters.Create(("ParentId", parentId)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<AdministrativeRegionChildResponse>>.Success(
            rows.Select(row => new AdministrativeRegionChildResponse(
                row.Id,
                row.ParentId,
                row.Code,
                row.Name,
                row.Level,
                row.DisplayOrder,
                row.ChildCount > 0))
                .ToArray());
    }

    /// <summary>构建嵌套树；默认最大深度 3，上限 5。</summary>
    public async Task<Result<IReadOnlyList<AdministrativeRegionTreeNodeResponse>>> GetTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        maxDepth = Math.Clamp(maxDepth, 1, 5);
        var rows = await queryExecutor.QueryAsync<AdministrativeRegionTreeRecord>(
                AdministrativeRegionSql.ListAllForTree,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var roots = new List<AdministrativeRegionTreeRecord>();
        var childrenByParentId = new Dictionary<Guid, AdministrativeRegionTreeRecord[]>();
        foreach (var group in rows.GroupBy(row => row.ParentId))
        {
            if (group.Key is null)
            {
                roots.AddRange(group);
                continue;
            }

            childrenByParentId[group.Key.Value] = group.ToArray();
        }

        IReadOnlyList<AdministrativeRegionTreeNodeResponse> Build(Guid? currentParentId, int depth)
        {
            AdministrativeRegionTreeRecord[] children;
            if (currentParentId is null)
            {
                children = roots.ToArray();
            }
            else if (!childrenByParentId.TryGetValue(currentParentId.Value, out children!))
            {
                return [];
            }

            if (depth > maxDepth)
            {
                return [];
            }

            return children
                .OrderBy(child => child.DisplayOrder)
                .ThenBy(child => child.Name, StringComparer.Ordinal)
                .ThenBy(child => child.Code, StringComparer.Ordinal)
                .Select(child => new AdministrativeRegionTreeNodeResponse(
                    child.Id,
                    child.ParentId,
                    child.Code,
                    child.Name,
                    child.Level,
                    child.DisplayOrder,
                    depth < maxDepth ? Build(child.Id, depth + 1) : []))
                .ToArray();
        }

        return Result<IReadOnlyList<AdministrativeRegionTreeNodeResponse>>.Success(
            parentId is null ? Build(null, 1) : Build(parentId, 1));
    }

    /// <summary>查询指定数据集键最近一次应用的清单记录。</summary>
    public async Task<Result<AdministrativeRegionDatasetManifestResponse>> GetLatestManifestAsync(
        string datasetKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetKey))
        {
            return ValidationFailure();
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<DatasetManifestRecord>(
                ResolveLatestManifestStatement(),
                RegionsSqlParameters.Create(("DatasetKey", datasetKey.Trim())),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return ManifestNotFound();
        }

        return Result<AdministrativeRegionDatasetManifestResponse>.Success(MapManifest(record));
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AdministrativeRegionSql.ListSqlServer,
            DatabaseProvider.MySql => AdministrativeRegionSql.ListMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };

    private SqlStatement ResolveLatestManifestStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AdministrativeRegionSql.FindLatestManifest,
            DatabaseProvider.MySql => AdministrativeRegionSql.FindLatestManifestMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };

    private static AdministrativeRegionResponse Map(AdministrativeRegionRecord record) =>
        new(
            record.Id,
            record.ParentId,
            record.Code,
            record.Name,
            record.ShortName,
            record.MergerName,
            record.ZipCode,
            record.CityCode,
            record.Level,
            record.RegionType,
            record.PinYin,
            record.Longitude,
            record.Latitude,
            record.DisplayOrder,
            record.Remark,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static AdministrativeRegionDatasetManifestResponse MapManifest(DatasetManifestRecord record) =>
        new(
            record.Id,
            record.DatasetKey,
            record.DatasetVersion,
            record.SourceDigest,
            record.RecordCount,
            record.AppliedAtUtc,
            record.AppliedByUserId);

    private static Result<AdministrativeRegionResponse> NotFound() =>
        Result<AdministrativeRegionResponse>.Failure(
            new Error(
                RegionsErrorCodes.NotFound,
                "Administrative region was not found.",
                ErrorType.NotFound));

    private static Result<AdministrativeRegionDatasetManifestResponse> ManifestNotFound() =>
        Result<AdministrativeRegionDatasetManifestResponse>.Failure(
            new Error(
                RegionsErrorCodes.NotFound,
                "Dataset manifest was not found.",
                ErrorType.NotFound));

    private static Result<AdministrativeRegionDatasetManifestResponse> ValidationFailure() =>
        Result<AdministrativeRegionDatasetManifestResponse>.Failure(
            new Error(
                RegionsErrorCodes.ValidationFailed,
                "Dataset key is required.",
                ErrorType.Validation));
}
