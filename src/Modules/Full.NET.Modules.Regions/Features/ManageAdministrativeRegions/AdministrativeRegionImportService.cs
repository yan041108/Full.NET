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

/// <summary>行政区域数据集导入预览与应用服务。</summary>
internal sealed class AdministrativeRegionImportService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>预览导入差异，不写入数据库。</summary>
    public async Task<Result<ImportAdministrativeRegionsPreviewResponse>> PreviewAsync(
        ImportAdministrativeRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateRequestShell(request, out var mergeMode, out var validationError))
        {
            return Result<ImportAdministrativeRegionsPreviewResponse>.Failure(validationError!);
        }

        var existingByCode = await LoadExistingSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var (normalizedItems, skippedCount) = NormalizeImportItems(request.Items, existingByCode);
        if (AdministrativeRegionTreeRules.ImportWouldCreateCycle(
                normalizedItems.ToDictionary(
                    item => item.Code,
                    item => new ImportRegionItemSnapshot(item.Code, item.ParentCode),
                    StringComparer.Ordinal)))
        {
            return Result<ImportAdministrativeRegionsPreviewResponse>.Failure(
                new Error(
                    RegionsErrorCodes.ImportInvalid,
                    "Import dataset contains parent cycles.",
                    ErrorType.Validation));
        }

        return Result<ImportAdministrativeRegionsPreviewResponse>.Success(
            AdministrativeRegionImportDiffEngine.Compute(
                existingByCode,
                normalizedItems,
                mergeMode,
                skippedCount));
    }

    /// <summary>校验并应用导入数据集，同时记录清单。</summary>
    public Task<Result<ImportAdministrativeRegionsApplyResponse>> ApplyAsync(
        ImportAdministrativeRegionsRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ApplyCoreAsync(request, actorUserId, token),
            cancellationToken);

    private async Task<Result<ImportAdministrativeRegionsApplyResponse>> ApplyCoreAsync(
        ImportAdministrativeRegionsRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(request, cancellationToken).ConfigureAwait(false);
        if (!preview.IsSuccess)
        {
            return Result<ImportAdministrativeRegionsApplyResponse>.Failure(preview.Error!);
        }

        if (!TryValidateRequestShell(request, out var mergeMode, out var validationError))
        {
            return Result<ImportAdministrativeRegionsApplyResponse>.Failure(validationError!);
        }

        var existingByCode = await LoadExistingSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var (normalizedItems, skippedCount) = NormalizeImportItems(request.Items, existingByCode);
        var importByCode = normalizedItems.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var codeToId = await LoadCodeToIdMapAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var addedCount = 0;
        var updatedCount = 0;

        foreach (var item in normalizedItems.OrderBy(item => item.Level).ThenBy(item => item.Code, StringComparer.Ordinal))
        {
            var parentId = ResolveParentId(item.ParentCode, codeToId, importByCode);
            if (item.ParentCode is not null && parentId is null)
            {
                return Result<ImportAdministrativeRegionsApplyResponse>.Failure(
                    new Error(
                        RegionsErrorCodes.ImportParentUnresolved,
                        $"Parent code '{item.ParentCode}' could not be resolved.",
                        ErrorType.Validation));
            }

            if (!existingByCode.ContainsKey(item.Code))
            {
                var id = idGenerator.NewId();
                await commandExecutor.ExecuteAsync(
                        AdministrativeRegionSql.Insert,
                        RegionsSqlParameters.Create(
                            ("Id", id),
                            ("ParentId", parentId),
                            ("Code", item.Code),
                            ("Name", item.Name),
                            ("ShortName", item.ShortName),
                            ("MergerName", item.MergerName),
                            ("ZipCode", item.ZipCode),
                            ("CityCode", item.CityCode),
                            ("Level", item.Level),
                            ("RegionType", item.RegionType),
                            ("PinYin", item.PinYin),
                            ("Longitude", item.Longitude),
                            ("Latitude", item.Latitude),
                            ("DisplayOrder", item.DisplayOrder),
                            ("Remark", null),
                            ("CreatedAtUtc", now),
                            ("Version", 1)),
                        cancellationToken)
                    .ConfigureAwait(false);
                codeToId[item.Code] = id;
                addedCount++;
                continue;
            }

            var regionId = codeToId[item.Code];
            await commandExecutor.ExecuteAsync(
                    AdministrativeRegionSql.UpdateImport,
                    RegionsSqlParameters.Create(
                        ("Id", regionId),
                        ("ParentId", parentId),
                        ("Name", item.Name),
                        ("ShortName", item.ShortName),
                        ("MergerName", item.MergerName),
                        ("ZipCode", item.ZipCode),
                        ("CityCode", item.CityCode),
                        ("Level", item.Level),
                        ("RegionType", item.RegionType),
                        ("PinYin", item.PinYin),
                        ("Longitude", item.Longitude),
                        ("Latitude", item.Latitude),
                        ("DisplayOrder", item.DisplayOrder),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            updatedCount++;
        }

        var removedCount = 0;
        if (string.Equals(mergeMode, AdministrativeRegionImportMergeModes.Replace, StringComparison.Ordinal)
            && preview.Value!.Removed.Count > 0)
        {
            var codes = preview.Value.Removed.Select(item => item.Code).ToArray();
            removedCount = await commandExecutor.ExecuteAsync(
                    ResolveDeleteByCodesStatement(),
                    RegionsSqlParameters.Create(("Codes", codes)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var manifestId = idGenerator.NewId();
        var appliedAt = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                AdministrativeRegionSql.InsertManifest,
                RegionsSqlParameters.Create(
                    ("Id", manifestId),
                    ("DatasetKey", request.DatasetKey.Trim()),
                    ("DatasetVersion", request.DatasetVersion.Trim()),
                    ("SourceDigest", request.SourceDigest.Trim()),
                    ("RecordCount", normalizedItems.Count),
                    ("AppliedAtUtc", appliedAt),
                    ("AppliedByUserId", actorUserId)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<ImportAdministrativeRegionsApplyResponse>.Success(
            new ImportAdministrativeRegionsApplyResponse(
                addedCount,
                updatedCount,
                removedCount,
                skippedCount,
                new AdministrativeRegionDatasetManifestResponse(
                    manifestId,
                    request.DatasetKey.Trim(),
                    request.DatasetVersion.Trim(),
                    request.SourceDigest.Trim(),
                    normalizedItems.Count,
                    appliedAt,
                    actorUserId)));
    }

    private async Task<Dictionary<string, AdministrativeRegionDiffSnapshot>> LoadExistingSnapshotsAsync(
        CancellationToken cancellationToken)
    {
        var links = await queryExecutor.QueryAsync<AdministrativeRegionCodeLinkRecord>(
                AdministrativeRegionSql.ListCodeLinks,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var snapshots = new Dictionary<string, AdministrativeRegionDiffSnapshot>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            var record = await queryExecutor.QuerySingleOrDefaultAsync<AdministrativeRegionRecord>(
                    AdministrativeRegionSql.FindByCode,
                    RegionsSqlParameters.Create(("Code", link.Code)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (record is null)
            {
                continue;
            }

            snapshots[record.Code] = new AdministrativeRegionDiffSnapshot(
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
                link.ParentCode);
        }

        return snapshots;
    }

    private async Task<Dictionary<string, Guid>> LoadCodeToIdMapAsync(CancellationToken cancellationToken)
    {
        var links = await queryExecutor.QueryAsync<AdministrativeRegionCodeLinkRecord>(
                AdministrativeRegionSql.ListCodeLinks,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return links.ToDictionary(link => link.Code, link => link.Id, StringComparer.Ordinal);
    }

    private static (List<AdministrativeRegionDiffSnapshot> Items, int SkippedCount) NormalizeImportItems(
        IReadOnlyList<ImportAdministrativeRegionItem> items,
        IReadOnlyDictionary<string, AdministrativeRegionDiffSnapshot> existingByCode)
    {
        var normalized = new List<AdministrativeRegionDiffSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var item in items)
        {
            if (!AdministrativeRegionCodeRules.TryNormalize(item.Code, out var code)
                || string.IsNullOrWhiteSpace(item.Name)
                || !AdministrativeRegionCodeRules.IsValidLevel(item.Level))
            {
                skipped++;
                continue;
            }

            if (!seen.Add(code))
            {
                skipped++;
                continue;
            }

            string? parentCode = null;
            if (!string.IsNullOrWhiteSpace(item.ParentCode))
            {
                if (!AdministrativeRegionCodeRules.TryNormalize(item.ParentCode, out var normalizedParent))
                {
                    skipped++;
                    continue;
                }

                parentCode = normalizedParent;
            }

            normalized.Add(new AdministrativeRegionDiffSnapshot(
                code,
                item.Name.Trim(),
                TrimOptional(item.ShortName),
                TrimOptional(item.MergerName),
                TrimOptional(item.ZipCode),
                TrimOptional(item.CityCode),
                item.Level,
                TrimOptional(item.RegionType),
                TrimOptional(item.PinYin),
                item.Longitude,
                item.Latitude,
                item.DisplayOrder ?? (existingByCode.TryGetValue(code, out var existing)
                    ? existing.DisplayOrder
                    : 0),
                parentCode));
        }

        return (normalized, skipped);
    }

    private static Guid? ResolveParentId(
        string? parentCode,
        IReadOnlyDictionary<string, Guid> codeToId,
        IReadOnlyDictionary<string, AdministrativeRegionDiffSnapshot> importByCode)
    {
        if (string.IsNullOrWhiteSpace(parentCode))
        {
            return null;
        }

        if (!AdministrativeRegionCodeRules.TryNormalize(parentCode, out var normalizedParent))
        {
            return null;
        }

        if (codeToId.TryGetValue(normalizedParent, out var parentId))
        {
            return parentId;
        }

        return null;
    }

    private static bool TryValidateRequestShell(
        ImportAdministrativeRegionsRequest request,
        out string mergeMode,
        out Error? validationError)
    {
        mergeMode = request.MergeMode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.DatasetKey)
            || string.IsNullOrWhiteSpace(request.DatasetVersion)
            || string.IsNullOrWhiteSpace(request.SourceDigest)
            || request.Items is null
            || request.Items.Count == 0)
        {
            validationError = new Error(
                RegionsErrorCodes.ImportInvalid,
                "Import request is invalid.",
                ErrorType.Validation);
            return false;
        }

        if (!DatasetVersionSortKey.TryParse(request.DatasetVersion, out _))
        {
            validationError = new Error(
                RegionsErrorCodes.ImportInvalid,
                "Dataset version format is invalid.",
                ErrorType.Validation);
            return false;
        }

        if (!string.Equals(mergeMode, AdministrativeRegionImportMergeModes.Merge, StringComparison.Ordinal)
            && !string.Equals(mergeMode, AdministrativeRegionImportMergeModes.Replace, StringComparison.Ordinal))
        {
            validationError = new Error(
                RegionsErrorCodes.ImportInvalid,
                "Merge mode must be merge or replace.",
                ErrorType.Validation);
            return false;
        }

        validationError = null;
        return true;
    }

    private static string? TrimOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private SqlStatement ResolveDeleteByCodesStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AdministrativeRegionSql.DeleteByCodesSqlServer,
            DatabaseProvider.MySql => AdministrativeRegionSql.DeleteByCodesMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
}
