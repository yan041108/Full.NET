using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Regions.Contracts;
using Full.NET.Modules.Regions.Domain;
using Full.NET.Modules.Regions.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Regions.Features.ManageAdministrativeRegions;

/// <summary>行政区域创建、更新与级联删除管理服务。</summary>
internal sealed class AdministrativeRegionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    AdministrativeRegionQueryService queries,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>创建行政区域节点。</summary>
    public Task<Result<AdministrativeRegionResponse>> CreateAsync(
        CreateAdministrativeRegionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    /// <summary>更新行政区域节点，使用乐观并发版本号。</summary>
    public Task<Result<AdministrativeRegionResponse>> UpdateAsync(
        Guid regionId,
        UpdateAdministrativeRegionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(regionId, request, token),
            cancellationToken);

    /// <summary>删除行政区域节点及其全部子孙节点。</summary>
    public Task<Result<bool>> DeleteAsync(
        Guid regionId,
        DeleteAdministrativeRegionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Result<bool>.Failure(InvalidError()));
        }

        return transaction.ExecuteAsync(
            token => DeleteCoreAsync(regionId, request.Version, token),
            cancellationToken);
    }

    private async Task<Result<AdministrativeRegionResponse>> CreateCoreAsync(
        CreateAdministrativeRegionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateWriteRequest(request.Code, request.Name, request.Level, out var code, out var name))
        {
            return Invalid();
        }

        if (request.ParentId is Guid parentId)
        {
            var parentError = await EnsureParentExistsAsync(parentId, cancellationToken)
                .ConfigureAwait(false);
            if (parentError is not null)
            {
                return parentError;
            }
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<AdministrativeRegionRecord>(
                AdministrativeRegionSql.FindByCode,
                RegionsSqlParameters.Create(("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeExists();
        }

        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                AdministrativeRegionSql.Insert,
                RegionsSqlParameters.Create(
                    ("Id", id),
                    ("ParentId", request.ParentId),
                    ("Code", code),
                    ("Name", name),
                    ("ShortName", NormalizeOptional(request.ShortName)),
                    ("MergerName", NormalizeOptional(request.MergerName)),
                    ("ZipCode", NormalizeOptional(request.ZipCode)),
                    ("CityCode", NormalizeOptional(request.CityCode)),
                    ("Level", request.Level),
                    ("RegionType", NormalizeOptional(request.RegionType)),
                    ("PinYin", NormalizeOptional(request.PinYin)),
                    ("Longitude", request.Longitude),
                    ("Latitude", request.Latitude),
                    ("DisplayOrder", request.DisplayOrder),
                    ("Remark", NormalizeOptional(request.Remark)),
                    ("CreatedAtUtc", now),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<AdministrativeRegionResponse>> UpdateCoreAsync(
        Guid regionId,
        UpdateAdministrativeRegionRequest request,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(regionId, cancellationToken).ConfigureAwait(false)
            is { IsSuccess: false })
        {
            return NotFound();
        }

        if (!TryValidateNameAndLevel(request.Name, request.Level, out var name))
        {
            return Invalid();
        }

        if (request.Version < 1)
        {
            return Invalid();
        }

        if (request.ParentId == regionId)
        {
            return InvalidParent();
        }

        if (request.ParentId is Guid parentId)
        {
            var parentError = await EnsureParentExistsAsync(parentId, cancellationToken)
                .ConfigureAwait(false);
            if (parentError is not null)
            {
                return parentError;
            }

            if (await WouldCreateParentCycleAsync(regionId, parentId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return ParentCycle();
            }
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                AdministrativeRegionSql.Update,
                RegionsSqlParameters.Create(
                    ("Id", regionId),
                    ("ParentId", request.ParentId),
                    ("Name", name),
                    ("ShortName", NormalizeOptional(request.ShortName)),
                    ("MergerName", NormalizeOptional(request.MergerName)),
                    ("ZipCode", NormalizeOptional(request.ZipCode)),
                    ("CityCode", NormalizeOptional(request.CityCode)),
                    ("Level", request.Level),
                    ("RegionType", NormalizeOptional(request.RegionType)),
                    ("PinYin", NormalizeOptional(request.PinYin)),
                    ("Longitude", request.Longitude),
                    ("Latitude", request.Latitude),
                    ("DisplayOrder", request.DisplayOrder),
                    ("Remark", NormalizeOptional(request.Remark)),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(regionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid regionId,
        int version,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(regionId, cancellationToken).ConfigureAwait(false)
            is { IsSuccess: false })
        {
            return Result<bool>.Failure(NotFoundError());
        }

        var affected = await commandExecutor.ExecuteAsync(
                ResolveDeleteSubtreeStatement(),
                RegionsSqlParameters.Create(("Id", regionId), ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected >= 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(VersionConflictError());
    }

    private async Task<Result<AdministrativeRegionResponse>?> EnsureParentExistsAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        var parent = await queryExecutor.QuerySingleOrDefaultAsync<AdministrativeRegionRecord>(
                AdministrativeRegionSql.FindById,
                RegionsSqlParameters.Create(("Id", parentId)),
                cancellationToken)
            .ConfigureAwait(false);
        return parent is null ? InvalidParent() : null;
    }

    private async Task<bool> WouldCreateParentCycleAsync(
        Guid regionId,
        Guid newParentId,
        CancellationToken cancellationToken)
    {
        var links = await queryExecutor.QueryAsync<AdministrativeRegionParentLinkRecord>(
                AdministrativeRegionSql.ListParentLinks,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return AdministrativeRegionTreeRules.WouldCreateParentCycle(
            regionId,
            newParentId,
            links.Select(link => new AdministrativeRegionParentLink(link.Id, link.ParentId)).ToArray());
    }

    private static bool TryValidateWriteRequest(
        string? codeValue,
        string? nameValue,
        int level,
        out string code,
        out string name)
    {
        code = string.Empty;
        name = nameValue?.Trim() ?? string.Empty;
        return AdministrativeRegionCodeRules.TryNormalize(codeValue, out code)
            && name.Length is >= 1 and <= 128
            && AdministrativeRegionCodeRules.IsValidLevel(level);
    }

    private static bool TryValidateNameAndLevel(string? nameValue, int level, out string name)
    {
        name = nameValue?.Trim() ?? string.Empty;
        return name.Length is >= 1 and <= 128 && AdministrativeRegionCodeRules.IsValidLevel(level);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private SqlStatement ResolveDeleteSubtreeStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AdministrativeRegionSql.DeleteSubtreeSqlServer,
            DatabaseProvider.MySql => AdministrativeRegionSql.DeleteSubtreeMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };

    private static Result<AdministrativeRegionResponse> Invalid() =>
        Result<AdministrativeRegionResponse>.Failure(InvalidError());

    private static Result<AdministrativeRegionResponse> NotFound() =>
        Result<AdministrativeRegionResponse>.Failure(NotFoundError());

    private static Result<AdministrativeRegionResponse> CodeExists() =>
        Result<AdministrativeRegionResponse>.Failure(
            new Error(RegionsErrorCodes.CodeExists, "Region code already exists.", ErrorType.Conflict));

    private static Result<AdministrativeRegionResponse> InvalidParent() =>
        Result<AdministrativeRegionResponse>.Failure(
            new Error(RegionsErrorCodes.InvalidParent, "Parent region is invalid.", ErrorType.Validation));

    private static Result<AdministrativeRegionResponse> ParentCycle() =>
        Result<AdministrativeRegionResponse>.Failure(
            new Error(RegionsErrorCodes.ParentCycle, "Parent assignment would create a cycle.", ErrorType.BusinessRule));

    private static Result<AdministrativeRegionResponse> VersionConflict() =>
        Result<AdministrativeRegionResponse>.Failure(VersionConflictError());

    private static Error InvalidError() =>
        new(RegionsErrorCodes.ValidationFailed, "The administrative region request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(RegionsErrorCodes.NotFound, "Administrative region was not found.", ErrorType.NotFound);

    private static Error VersionConflictError() =>
        new(RegionsErrorCodes.ConcurrencyConflict, "Region was updated by another operation.", ErrorType.Conflict);
}
