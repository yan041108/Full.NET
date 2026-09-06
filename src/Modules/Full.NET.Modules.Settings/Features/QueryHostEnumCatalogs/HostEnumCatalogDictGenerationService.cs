using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostDictItems;
using Full.NET.Modules.Settings.Features.ManageHostDictTypes;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.Modules.Settings.Features.QueryHostEnumCatalogs;

/// <summary>
/// 将已登记枚举目录预览并生成 Host 数据字典；遵循种子写入语义，不覆盖人工字典项标签。
/// </summary>
internal sealed class HostEnumCatalogDictGenerationService(
    EnumCatalogRegistry registry,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>预览枚举目录到 Host 字典的生成计划。</summary>
    /// <param name="catalogKey">枚举目录键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<EnumCatalogDictGenerationPreview>> PreviewAsync(
        string catalogKey,
        CancellationToken cancellationToken = default)
    {
        var catalog = registry.FindByKey(catalogKey);
        if (catalog is null)
        {
            return NotFound();
        }

        var (existingType, existingItems) = await LoadDictStateAsync(
                catalog.Key,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<EnumCatalogDictGenerationPreview>.Success(
            HostEnumCatalogDictGenerationPlanner.Plan(
                catalog,
                existingType,
                existingItems));
    }

    /// <summary>
    /// 按预览计划幂等写入 Host 字典；仅创建缺失类型与字典项，跳过冲突与非法值。
    /// </summary>
    /// <param name="catalogKey">枚举目录键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<EnumCatalogDictGenerationResult>> GenerateAsync(
        string catalogKey,
        CancellationToken cancellationToken = default)
    {
        var catalog = registry.FindByKey(catalogKey);
        if (catalog is null)
        {
            return Result<EnumCatalogDictGenerationResult>.Failure(NotFoundError());
        }

        var (existingType, existingItems) = await LoadDictStateAsync(
                catalog.Key,
                cancellationToken)
            .ConfigureAwait(false);

        var preview = HostEnumCatalogDictGenerationPlanner.Plan(
            catalog,
            existingType,
            existingItems);

        var dictTypeCreated = false;
        Guid dictTypeId;
        if (existingType is null)
        {
            dictTypeId = idGenerator.NewId();
            var now = clock.UtcNow;
            await commandExecutor.ExecuteAsync(
                    DictTypeSql.Insert,
                    SettingsSqlParameters.Create(
                        ("Id", dictTypeId),
                        ("Code", catalog.Key),
                        ("Name", catalog.DisplayName),
                        ("Description", catalog.Description),
                        ("DisplayOrder", 0),
                        ("IsActive", true),
                        ("CreatedAtUtc", now),
                        ("Version", 1)),
                    cancellationToken)
                .ConfigureAwait(false);
            dictTypeCreated = true;
        }
        else
        {
            dictTypeId = existingType.Id;
        }

        var itemsCreated = 0;
        foreach (var item in preview.Items)
        {
            if (!string.Equals(
                    item.Action,
                    EnumCatalogDictGenerationItemActions.Create,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var existingItem = await queryExecutor.QuerySingleOrDefaultAsync<DictItemIdentityRecord>(
                    DictItemSql.FindByTypeAndValue,
                    SettingsSqlParameters.Create(
                        ("DictTypeId", dictTypeId),
                        ("Value", item.Value)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingItem is not null)
            {
                continue;
            }

            await commandExecutor.ExecuteAsync(
                    DictItemSql.Insert,
                    SettingsSqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("DictTypeId", dictTypeId),
                        ("Label", item.ProposedLabel),
                        ("Value", item.Value),
                        ("Color", null),
                        ("DisplayOrder", item.DisplayOrder),
                        ("IsActive", true),
                        ("CreatedAtUtc", clock.UtcNow),
                        ("Version", 1)),
                    cancellationToken)
                .ConfigureAwait(false);
            itemsCreated++;
        }

        var finalPreview = HostEnumCatalogDictGenerationPlanner.Plan(
            catalog,
            new DictTypeIdentityRecord(
                dictTypeId,
                catalog.Key,
                catalog.DisplayName,
                catalog.Description,
                0,
                true,
                1),
            await LoadDictItemsAsync(dictTypeId, cancellationToken).ConfigureAwait(false));

        return Result<EnumCatalogDictGenerationResult>.Success(
            new EnumCatalogDictGenerationResult(
                catalog.Key,
                catalog.Key,
                dictTypeId,
                dictTypeCreated,
                itemsCreated,
                CountByAction(finalPreview, EnumCatalogDictGenerationItemActions.SkipExists),
                CountByAction(finalPreview, EnumCatalogDictGenerationItemActions.ConflictLabel),
                CountByAction(finalPreview, EnumCatalogDictGenerationItemActions.InvalidValue),
                finalPreview.Items));
    }

    private async Task<(DictTypeIdentityRecord? Type, IReadOnlyList<DictItemIdentityRecord> Items)> LoadDictStateAsync(
        string dictTypeCode,
        CancellationToken cancellationToken)
    {
        var existingType = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                DictTypeSql.FindByCode,
                SettingsSqlParameters.Create(("Code", dictTypeCode)),
                cancellationToken)
            .ConfigureAwait(false);

        if (existingType is null)
        {
            return (null, []);
        }

        var items = await LoadDictItemsAsync(existingType.Id, cancellationToken)
            .ConfigureAwait(false);
        return (existingType, items);
    }

    private async Task<IReadOnlyList<DictItemIdentityRecord>> LoadDictItemsAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<DictItemIdentityRecord>(
                DictItemSql.ListAllByTypeId,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        return rows.ToArray();
    }

    private static int CountByAction(
        EnumCatalogDictGenerationPreview preview,
        string action) =>
        preview.Items.Count(item => string.Equals(item.Action, action, StringComparison.Ordinal));

    private static Result<EnumCatalogDictGenerationPreview> NotFound() =>
        Result<EnumCatalogDictGenerationPreview>.Failure(NotFoundError());

    private static Error NotFoundError() =>
        new(
            SettingsErrorCodes.EnumCatalogNotFound,
            "The enumeration or constant catalog was not found.",
            ErrorType.NotFound);
}
